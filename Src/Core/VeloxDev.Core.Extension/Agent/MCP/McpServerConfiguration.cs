using VeloxDev.MVVM;

using System.Collections.Generic;
namespace VeloxDev.AI.MCP;

/// <summary>The configuration of one MCP server.</summary>
public partial class McpServerConfiguration
{
    [VeloxProperty] public partial string Name { get; set; }
    [VeloxProperty] public partial string Description { get; set; }

    /// <summary>
    /// How the server is reached.
    /// <see cref="McpServerRunMode.Npm"/> requires npm-installed package;
    /// <see cref="McpServerRunMode.Npx"/>, <see cref="McpServerRunMode.Uvx"/>, and <see cref="McpServerRunMode.Pip"/>
    /// use <see cref="Package"/> as the package name directly;
    /// <see cref="McpServerRunMode.Dotnet"/> executes via <c>dotnet {Package}</c>;
    /// <see cref="McpServerRunMode.Exe"/> executes <c>{Package}</c> directly (tech-agnostic);
    /// <see cref="McpServerRunMode.Http"/> connects to a remote server over HTTP using <see cref="Endpoint"/>.
    /// </summary>
    [VeloxProperty] public partial McpServerRunMode RunMode { get; set; }

    /// <summary>
    /// Package name / directory name.
    /// Npm/Npx/Uvx/Pip: an NPM or PyPI package name;
    /// Dotnet: a DLL path under mcpRoot, e.g. "sharp-email-mcp/SharpEmailMcp.dll";
    /// Exe: an executable path under mcpRoot, e.g. "tools/my-tool.exe".
    /// </summary>
    [VeloxProperty] public partial string Package { get; set; }

    /// <summary>
    /// Version tag. When null, "latest" is used.
    /// Applies to <see cref="McpServerRunMode.Npm"/> and <see cref="McpServerRunMode.Pip"/> modes.
    /// </summary>
    [VeloxProperty] public partial string? Version { get; set; }

    /// <summary>Extra arguments passed to the server process (e.g. an allowed-directory set for a filesystem server)</summary>
    [VeloxProperty] public partial string[] Arguments { get; set; }

    /// <summary>
    /// Remote MCP endpoint URL (only for <see cref="McpServerRunMode.Http"/> mode), e.g. "https://mcp.example.com/mcp".
    /// Connects via Streamable HTTP (older servers fall back to SSE automatically).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Arbitrary server options, keyed by the option's name. A value is a primitive, a nested
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> for a sub-section, or an array of primitives.
    /// <code>
    /// Options = new Dictionary&lt;string, object?&gt;
    /// {
    ///     ["headers"] = new Dictionary&lt;string, object?&gt; { ["Authorization"] = "Bearer x", ["X-Custom"] = "v" },
    ///     ["env"] = new Dictionary&lt;string, object?&gt; { ["FILESYSTEM_ROOT"] = "C:/data", ["API_KEY"] = "k" },
    ///     ["connectionTimeout"] = 30,                // seconds (or a TimeSpan string)
    ///     ["transportMode"] = "StreamableHttp",      // Http: AutoDetect/StreamableHttp/Sse
    ///     ["ownsSession"] = true,                    // Http: whether to hold the MCP session (stateful)
    ///     ["workingDirectory"] = "C:/data",          // stdio working directory
    ///     ["oauth"] = new Dictionary&lt;string, object?&gt;
    ///     {
    ///         ["clientId"] = "id", ["clientSecret"] = "s",
    ///         ["redirectUri"] = "http://localhost:1179/cb", ["scopes"] = new[] { "read" },
    ///     },
    /// };
    /// </code>
    /// A dictionary rather than an anonymous object because the archive format's world is closed: the set of
    /// types it can write is the set the generator was compiled over, and an anonymous type is in neither. A map
    /// says the same thing and needs no reflection to read.
    /// </summary>
    /// <remarks>
    /// Unknown keys are rejected by <see cref="McpScope"/> — an error rather than a silent ignore — so typos
    /// surface immediately.
    /// </remarks>
    public IReadOnlyDictionary<string, object?>? Options { get; set; }
}
