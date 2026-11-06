using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VeloxDev.AI.MCP;

/// <summary>
/// Reads the common <c>.mcp.json</c> shape into <see cref="McpServerConfiguration"/> entries.
/// </summary>
/// <remarks>
/// <para>
/// The document is the one Claude Code and several other MCP clients use: a top-level
/// <c>mcpServers</c> object whose keys are server names and whose values carry either
/// <c>command</c> + <c>args</c> (a local stdio server) or <c>url</c> (a remote HTTP server).
/// A local entry becomes <see cref="McpServerRunMode.Normal"/> — its command runs exactly as written,
/// because that is the one form the configuration file can express without guessing an ecosystem.
/// Other run modes still exist for hosts that want VeloxDev to install or place the package.
/// </para>
/// <para>
/// Supported fields: <c>command</c>, <c>args</c>, <c>env</c>, <c>cwd</c>, <c>url</c>, <c>type</c>,
/// <c>headers</c>, <c>description</c>, <c>disabled</c>. Unknown fields are ignored; a <c>disabled</c>
/// entry is skipped. The result is intended for <see cref="McpScope.WithServers"/> or
/// <see cref="McpScope.LoadAsync"/>; it is not read from a file automatically.
/// </para>
/// </remarks>
public static class McpConfigurationFile
{
    /// <summary>The document's server-map property name.</summary>
    public const string ServersProperty = "mcpServers";

    // ${VAR} / ${VAR:-default}, the expansion the common .mcp.json readers apply. An undefined variable
    // with no default becomes empty, which is what lets a file opt a token out without rewriting it.
    private static readonly Regex VariablePattern = new(
        @"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?\}", RegexOptions.Compiled);

    /// <summary>Reads and parses an <c>.mcp.json</c> file.</summary>
    /// <param name="path">The document path.</param>
    /// <returns>One configuration per enabled server, in document order.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The document is not a valid MCP server map.</exception>
    public static IReadOnlyList<McpServerConfiguration> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A configuration path is required.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException($"The MCP configuration file was not found: {path}", path);
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses an <c>.mcp.json</c> document.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>One configuration per enabled server, in document order.</returns>
    /// <exception cref="ArgumentException"><paramref name="json"/> is empty.</exception>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    /// <exception cref="InvalidDataException">The document is not a valid MCP server map.</exception>
    public static IReadOnlyList<McpServerConfiguration> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("A configuration document is required.", nameof(json));

        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    /// <summary>Parses an already-open <c>.mcp.json</c> root element.</summary>
    /// <param name="root">The document root; it must carry an object-valued <c>mcpServers</c>.</param>
    /// <returns>One configuration per enabled server, in document order.</returns>
    /// <exception cref="InvalidDataException">The root has no <c>mcpServers</c> object or an entry is malformed.</exception>
    public static IReadOnlyList<McpServerConfiguration> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("An .mcp.json document must be a JSON object.");

        if (!root.TryGetProperty(ServersProperty, out var servers) || servers.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The document has no object-valued '{ServersProperty}' property.");

        var result = new List<McpServerConfiguration>();
        foreach (var entry in servers.EnumerateObject())
        {
            var config = ParseServer(entry.Name, entry.Value);
            if (config is not null) result.Add(config);
        }

        return result;
    }

    private static McpServerConfiguration? ParseServer(string name, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"MCP server '{name}' must be a JSON object.");

        if (entry.TryGetProperty("disabled", out var disabled)
            && disabled.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var url = Expand(OptionalString(entry, "url"));
        var command = Expand(OptionalString(entry, "command"));
        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(command))
            throw new InvalidDataException($"MCP server '{name}' names both 'url' and 'command'; use one.");

        var description = OptionalString(entry, "description") ?? name;

        if (!string.IsNullOrWhiteSpace(url))
        {
            var options = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (entry.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "sse", StringComparison.OrdinalIgnoreCase))
            {
                options["transportMode"] = "Sse";
            }
            if (entry.TryGetProperty("headers", out var headers))
                options["headers"] = ReadStringMap(headers, name, "headers");

            return new McpServerConfiguration
            {
                Name = name,
                Description = description,
                RunMode = McpServerRunMode.Http,
                Package = string.Empty,
                Endpoint = url,
                Arguments = [],
                Options = options.Count > 0 ? options : null,
            };
        }

        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidDataException($"MCP server '{name}' has neither 'command' nor 'url'.");

        var arguments = entry.TryGetProperty("args", out var argsElement)
            ? ReadStringArray(argsElement, name)
            : [];
        var stdioOptions = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (entry.TryGetProperty("env", out var env))
            stdioOptions["env"] = ReadStringMap(env, name, "env");
        var cwd = Expand(OptionalString(entry, "cwd"));
        if (!string.IsNullOrWhiteSpace(cwd))
            stdioOptions["workingDirectory"] = cwd;

        return new McpServerConfiguration
        {
            Name = name,
            Description = description,
            RunMode = McpServerRunMode.Normal,
            Package = command!,
            Arguments = arguments,
            Options = stdioOptions.Count > 0 ? stdioOptions : null,
        };
    }

    private static string? OptionalString(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] ReadStringArray(JsonElement value, string server)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"MCP server '{server}': 'args' must be an array.");

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
            list.Add(Expand(ScalarText(item)) ?? string.Empty);
        return [.. list];
    }

    private static Dictionary<string, object?> ReadStringMap(JsonElement value, string server, string field)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"MCP server '{server}': '{field}' must be an object.");

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var member in value.EnumerateObject())
            map[member.Name] = Expand(ScalarText(member.Value));
        return map;
    }

    private static string? Expand(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("${", StringComparison.Ordinal))
            return value;

        return VariablePattern.Replace(value, match =>
        {
            var fallback = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;
            return Environment.GetEnvironmentVariable(match.Groups[1].Value) ?? fallback;
        });
    }

    private static string ScalarText(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText(),
        };
}
