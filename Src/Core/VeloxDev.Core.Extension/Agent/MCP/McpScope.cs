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
/// The <see cref="McpServerConfiguration.Package"/> is a runtime-relative path:
/// <c>{root}/node/{Package}</c> (npm),
/// <c>{root}/py/{Package}</c> (Python),
/// <c>{root}/dotnet/{Package}</c> (.NET),
/// <c>{root}/exe/{Package}</c> (any executable).
/// For Dotnet mode, Package includes the DLL name, e.g. "sharp-email-mcp/SharpEmailMcp.dll".
/// For Exe mode, Package is the path to any executable, e.g. "tools/my-tool.exe".
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
                if (config.RunMode == McpServerRunMode.Npm)
                    await EnsureNpmPackageAsync(config.Package, config.Version, mcpRoot, ct);
                else if (config.RunMode == McpServerRunMode.Pip)
                    await EnsurePipPackageAsync(config.Package, config.Version, mcpRoot, ct);

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
    private static string GetRuntimeDir(McpServerRunMode mode) => mode switch
    {
        McpServerRunMode.Npm or McpServerRunMode.Npx => "node",
        McpServerRunMode.Pip or McpServerRunMode.Uvx  => "py",
        McpServerRunMode.Dotnet                       => "dotnet",
        McpServerRunMode.Exe                           => "exe",
        _ => "node",
    };

    /// <summary>
    /// Gets the working/installation directory for a configuration.
    /// <c>{mcpRoot}/{runtime}/{Package}</c>. If Package includes a filename
    /// (e.g. "sharp-email-mcp/SharpEmailMcp.dll"), uses the directory part.
    /// </summary>
    private static string GetPackageDir(McpServerConfiguration config, string mcpRoot)
    {
        var fullPath = Path.Combine(mcpRoot, GetRuntimeDir(config.RunMode), config.Package);
        return Path.HasExtension(fullPath)
            ? Path.GetDirectoryName(fullPath)!
            : fullPath;
    }

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
            McpServerRunMode.Exe    => BuildExeArgs(config, mcpRoot),
            _                       => BuildNpmArgs(config, mcpRoot),
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

    // ── npm: npm install + node ────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildNpmArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var pkgDir = Path.Combine(mcpRoot, "node", config.Package);
        var npmName = config.Package;
        var entry = npmName;
        var pkgJson = Path.Combine(pkgDir, "node_modules", npmName, "package.json");
        if (File.Exists(pkgJson))
        {
            var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(pkgJson));
            entry = doc.RootElement.TryGetProperty("main", out var main)
                ? main.GetString()
                : (doc.RootElement.TryGetProperty("bin", out var bin)
                    ? (bin.ValueKind == System.Text.Json.JsonValueKind.String
                        ? bin.GetString()
                        : bin.EnumerateObject().First().Value.GetString())
                    : null);
        }

        if (string.IsNullOrWhiteSpace(entry))
            throw new FileNotFoundException(
                "MCP server entry not found. Ensure package.json has 'main' or 'bin' field. Package dir: " + pkgDir);

        var serverJs = Path.Combine(pkgDir, "node_modules", npmName, entry);
        if (!File.Exists(serverJs))
            throw new FileNotFoundException(
                "MCP server entry not found: " + serverJs +
                ". Run npm install first in: " + pkgDir);

        return ("node", BuildArgs(serverJs, config.Arguments));
    }

    // ── Node.js: npx ───────────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildNpxArgs(
        McpServerConfiguration config)
        => ("npx", BuildArgs("-y", config.Package, config.Arguments));

    // ── Python: uvx ────────────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildUvxArgs(
        McpServerConfiguration config)
        => ("uvx", BuildArgs(config.Package, config.Arguments));

    // ── .NET: dotnet ───────────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildDotnetArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var dllPath = Path.Combine(mcpRoot, "dotnet", config.Package);

        if (!File.Exists(dllPath))
            throw new FileNotFoundException(
                "MCP server DLL not found: " + dllPath +
                ". Publish the project to: " + Path.GetDirectoryName(dllPath));

        return ("dotnet", BuildArgs(dllPath, config.Arguments));
    }

    // ── Any executable: direct execution ──────────────────────────────────

    private static (string cmd, List<string> args) BuildExeArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var exePath = Path.Combine(mcpRoot, "exe", config.Package);

        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                "Executable not found: " + exePath +
                ". Place it under: " + Path.GetDirectoryName(exePath));

        // Command is the executable path itself; no interpreter prefix
        return (exePath, config.Arguments?.ToList() ?? []);
    }

    // ── Python: pip + venv ─────────────────────────────────────────────────

    private static (string cmd, List<string> args) BuildPipArgs(
        McpServerConfiguration config, string mcpRoot)
    {
        var venvDir = Path.Combine(mcpRoot, "py", "venvs", config.Package);
        var pythonExe = GetVenvPythonExe(venvDir);
        var module = config.Package.Replace("-", "_");

        return (pythonExe, BuildArgs("-m", module, config.Arguments));
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
