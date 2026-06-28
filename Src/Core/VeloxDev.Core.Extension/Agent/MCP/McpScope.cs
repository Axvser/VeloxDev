using CliWrap;
using CliWrap.Buffered;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace VeloxDev.AI.MCP;

/// <summary>
/// Local MCP environment configuration.
/// <para>
/// Manages the MCP installation root directory and provides <see cref="LoadAsync"/>
/// to install npm packages and connect to MCP servers via stdio.
/// </para>
/// <para>
/// The list of servers is supplied by the caller (e.g. AgentHelper) —
/// this scope does not maintain a server registry.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var tools = await new McpScope().LoadAsync(new[] {
///     new McpServerConfiguration { NpmPackage = "...", ... },
/// });
/// </code>
/// </example>
public class McpScope
{
    // ── Events ─────────────────────────────────────────────────────────────

    /// <summary>Raised when a server fails to load. The error is not rethrown.</summary>
    public event Action<McpServerConfiguration, Exception>? ServerError;

    // ── Local configuration ────────────────────────────────────────────────

    /// <summary>
    /// MCP installation root (relative to <see cref="AppContext.BaseDirectory"/>).
    /// Defaults to <c>".evn/mcp"</c>.
    /// </summary>
    public string McpRootRelative { get; private set; } = ".evn/mcp";

    // ── Internal state ─────────────────────────────────────────────────────

    private static readonly SemaphoreSlim s_installLock = new(1, 1);
    private static readonly List<string> s_installed = [];

    // ── Fluent configuration ───────────────────────────────────────────────

    /// <summary>
    /// Sets the MCP installation root (relative path).
    /// For example, <c>".evn/mcp"</c> resolves to <c>{AppContext.BaseDirectory}/.evn/mcp</c>.
    /// </summary>
    public McpScope WithMcpRoot(string relativePath)
    {
        McpRootRelative = relativePath;
        return this;
    }

    // ── Execution ──────────────────────────────────────────────────────────

    /// <summary>
    /// Installs npm packages (idempotent) and connects to the specified MCP servers,
    /// returning the merged list of <see cref="AITool"/> instances.
    /// Each server is connected via an independent stdio transport.
    /// <para>
    /// When a server fails, <see cref="ServerError"/> is raised and loading
    /// continues with the remaining servers.
    /// </para>
    /// </summary>
    /// <param name="servers">MCP server configurations to load. The list is maintained by the caller.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AITool[]> LoadAsync(
        IEnumerable<McpServerConfiguration> servers, CancellationToken ct = default)
    {
        var mcpRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, McpRootRelative));
        Directory.CreateDirectory(mcpRoot);

        var allTools = new List<AITool>();

        foreach (var config in servers)
        {
            if (config is null) continue;

            try
            {
                if (config.RunMode == McpServerRunMode.Node)
                    await EnsureNpmPackageAsync(config.NpmPackage, config.NpmVersion, mcpRoot, ct);

                var tools = await ConnectServerAsync(config, mcpRoot, ct);
                allTools.AddRange(tools);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ServerError?.Invoke(config, ex);
                // continue with remaining servers
            }
        }

        return [.. allTools];
    }

    // ── npm install (idempotent, thread-safe) ──────────────────────────────

    private static async Task EnsureNpmPackageAsync(
        string package, string? version, string root, CancellationToken ct)
    {
        var key = version is not null ? $"{package}@{version}" : package;
        if (s_installed.Contains(key)) return;

        await s_installLock.WaitAsync(ct);
        try
        {
            if (s_installed.Contains(key)) return;

            var ver = version ?? "latest";
            var packageJson = "{\"name\":\"mcp\",\"private\":true,\"dependencies\":{"
                + "\"" + package + "\":\"" + ver + "\"}}";
            File.WriteAllText(Path.Combine(root, "package.json"), packageJson);

            var result = await Cli.Wrap("npm")
                .WithArguments("install --no-audit --no-fund")
                .WithWorkingDirectory(root)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "npm install failed (exit " + result.ExitCode + "):\n" + result.StandardError);
            }

            s_installed.Add(key);
        }
        finally { s_installLock.Release(); }
    }

    // ── MCP protocol connection ────────────────────────────────────────────

    private static async Task<AITool[]> ConnectServerAsync(
        McpServerConfiguration config, string mcpRoot, CancellationToken ct)
    {
        var (cmd, args) = config.RunMode switch
        {
            McpServerRunMode.Npx => BuildNpxArgs(config),
            McpServerRunMode.Uvx => BuildUvxArgs(config),
            _                    => BuildNodeArgs(config, mcpRoot),
        };

        var client = await McpClient.CreateAsync(new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = config.Name,
                Command = cmd,
                Arguments = [.. args],
            }));

        var tools = await client.ListToolsAsync();
        return [.. tools.Cast<AITool>()];
    }

    private static (string cmd, List<string> args) BuildNodeArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var serverJs = Path.Combine(
            mcpRoot, "node_modules", config.NpmPackage, config.ServerModulePath);

        if (!File.Exists(serverJs))
        {
            throw new FileNotFoundException(
                "MCP server entry not found: " + serverJs);
        }

        var args = new List<string> { serverJs };
        if (config.ServerArguments is not null)
            args.AddRange(config.ServerArguments);

        return ("node", args);
    }

    private static (string cmd, List<string> args) BuildNpxArgs(
        McpServerConfiguration config)
    {
        var args = new List<string> { "-y", config.NpmPackage };
        if (config.ServerArguments is not null)
            args.AddRange(config.ServerArguments);

        return ("npx", args);
    }

    private static (string cmd, List<string> args) BuildUvxArgs(
        McpServerConfiguration config)
    {
        var args = new List<string> { config.NpmPackage };
        if (config.ServerArguments is not null)
            args.AddRange(config.ServerArguments);

        return ("uvx", args);
    }
}
