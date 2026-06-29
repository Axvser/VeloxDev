using CliWrap;
using CliWrap.Buffered;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace VeloxDev.AI.MCP;

/// <summary>
/// Local MCP environment configuration.
/// <para>
/// Manages the MCP installation root directory and provides <see cref="LoadAsync"/>
/// to install MCP server packages and connect via stdio.
/// </para>
/// <para>
/// Packages are isolated by runtime:
/// <c>{root}/node/{package}/</c> (npm),
/// <c>{root}/py/venvs/{package}/</c> (pip),
/// <c>{root}/dotnet/{package}/</c> (.NET).
/// </para>
/// </summary>
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

    public McpScope WithMcpRoot(string relativePath)
    {
        McpRootRelative = relativePath;
        return this;
    }

    // ── Execution ──────────────────────────────────────────────────────────

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
                else if (config.RunMode == McpServerRunMode.Pip)
                    await EnsurePipPackageAsync(config.NpmPackage, config.NpmVersion, mcpRoot, ct);

                var tools = await ConnectServerAsync(config, mcpRoot, ct);
                allTools.AddRange(tools);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ServerError?.Invoke(config, ex);
            }
        }

        return [.. allTools];
    }

    // ── Runtime directory helpers ──────────────────────────────────────────

    /// <summary>Returns the runtime-specific subdirectory name for a run mode.</summary>
    private static string GetRuntimePrefix(McpServerRunMode mode) => mode switch
    {
        McpServerRunMode.Node or McpServerRunMode.Npx => "node",
        McpServerRunMode.Pip or McpServerRunMode.Uvx  => "py",
        McpServerRunMode.Dotnet                       => "dotnet",
        _ => "node",
    };

    /// <summary>
    /// Gets the package directory for a config under <c>{mcpRoot}/{runtime}/{package}/</c>.
    /// For <see cref="McpServerRunMode.Node"/> this is also the npm working directory.
    /// For <see cref="McpServerRunMode.Pip"/> the venv lives under here.
    /// For <see cref="McpServerRunMode.Dotnet"/> the published files live here.
    /// </summary>
    private static string GetPackageDir(McpServerConfiguration config, string mcpRoot)
        => Path.Combine(mcpRoot, GetRuntimePrefix(config.RunMode), config.NpmPackage);

    // ── npm install (Node.js, isolated per package) ────────────────────────

    private static async Task EnsureNpmPackageAsync(
        string package, string? version, string mcpRoot, CancellationToken ct)
    {
        var key = "node:" + (version is not null ? $"{package}@{version}" : package);
        if (s_installed.Contains(key)) return;

        await s_installLock.WaitAsync(ct);
        try
        {
            if (s_installed.Contains(key)) return;

            var pkgDir = Path.Combine(mcpRoot, "node", package);
            Directory.CreateDirectory(pkgDir);

            var ver = version ?? "latest";
            var packageJson = "{\"name\":\"mcp-" + package + "\",\"private\":true,\"dependencies\":{"
                + "\"" + package + "\":\"" + ver + "\"}}";
            File.WriteAllText(Path.Combine(pkgDir, "package.json"), packageJson);

            var result = await Cli.Wrap("npm")
                .WithArguments("install --no-audit --no-fund")
                .WithWorkingDirectory(pkgDir)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    "npm install failed (exit " + result.ExitCode + "):\n" + result.StandardError);

            s_installed.Add(key);
        }
        finally { s_installLock.Release(); }
    }

    // ── pip + venv (Python, isolated) ─────────────────────────────────────

    private static async Task EnsurePipPackageAsync(
        string package, string? version, string mcpRoot, CancellationToken ct)
    {
        var key = "py:" + (version is not null ? $"{package}@{version}" : package);
        if (s_installed.Contains(key)) return;

        await s_installLock.WaitAsync(ct);
        try
        {
            if (s_installed.Contains(key)) return;

            var venvDir = Path.Combine(mcpRoot, "py", "venvs", package);
            var pythonExe = GetVenvPythonExe(venvDir);

            // Step 1: create venv
            if (!File.Exists(pythonExe))
            {
                Directory.CreateDirectory(venvDir);
                var createResult = await Cli.Wrap("python")
                    .WithArguments($"-m venv \"{venvDir}\"")
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync(ct);

                if (createResult.ExitCode != 0)
                    throw new InvalidOperationException(
                        "Failed to create venv:\n" + createResult.StandardError);
            }

            // Step 2: pip install 到 venv 内
            var ver = version ?? "";
            var installResult = await Cli.Wrap(pythonExe)
                .WithArguments($"-m pip install {package}{ver} --quiet")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            if (installResult.ExitCode != 0)
                throw new InvalidOperationException(
                    $"pip install {package} failed:\n" + installResult.StandardError);

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
            McpServerRunMode.Npx    => BuildNpxArgs(config),
            McpServerRunMode.Uvx    => BuildUvxArgs(config),
            McpServerRunMode.Dotnet => BuildDotnetArgs(config, mcpRoot),
            McpServerRunMode.Pip    => BuildPipArgs(config, mcpRoot),
            _                       => BuildNodeArgs(config, mcpRoot),
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

    // ── Node.js: npm install + node ────────────────────────────────────────

    private static (string cmd, List<string> args) BuildNodeArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var pkgDir = Path.Combine(mcpRoot, "node", config.NpmPackage);
        var serverJs = Path.Combine(pkgDir, "node_modules", config.NpmPackage, config.ServerModulePath);

        if (!File.Exists(serverJs))
            throw new FileNotFoundException(
                "MCP server entry not found: " + serverJs +
                ". Run npm install first in: " + pkgDir);

        return ("node", BuildArgs(serverJs, config.ServerArguments));
    }

    // ── Node.js: npx ───────────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildNpxArgs(
        McpServerConfiguration config)
        => ("npx", BuildArgs("-y", config.NpmPackage, config.ServerArguments));

    // ── Python: uvx ────────────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildUvxArgs(
        McpServerConfiguration config)
        => ("uvx", BuildArgs(config.NpmPackage, config.ServerArguments));

    // ── .NET: dotnet ───────────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildDotnetArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var dllPath = Path.Combine(mcpRoot, "dotnet", config.NpmPackage, config.ServerModulePath);

        if (!File.Exists(dllPath))
            throw new FileNotFoundException(
                "MCP server DLL not found: " + dllPath +
                ". Publish the project to: " + Path.GetDirectoryName(dllPath));

        return ("dotnet", BuildArgs(dllPath, config.ServerArguments));
    }

    // ── Python: pip + venv ─────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildPipArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var venvDir = Path.Combine(mcpRoot, "py", "venvs", config.NpmPackage);
        var pythonExe = GetVenvPythonExe(venvDir);
        var module = !string.IsNullOrWhiteSpace(config.ServerModulePath)
            ? config.ServerModulePath
            : config.NpmPackage;

        return (pythonExe, BuildArgs("-m", module, config.ServerArguments));
    }

    // ── Args helper ────────────────────────────────────────────────────────

    /// <summary>Returns the Python executable path inside a venv, cross-platform.</summary>
    private static string GetVenvPythonExe(string venvDir)
        => Path.Combine(venvDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine("Scripts", "python.exe")
            : Path.Combine("bin", "python"));

    private static List<string> BuildArgs(string first, params string?[]? rest)
    {
        var list = new List<string> { first };
        if (rest is not null)
            foreach (var r in rest)
                if (r is not null) list.Add(r);
        return list;
    }

    private static List<string> BuildArgs(string first, string second, string[]? rest)
    {
        var list = new List<string> { first, second };
        if (rest is not null)
            list.AddRange(rest);
        return list;
    }
}
