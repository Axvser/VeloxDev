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
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using Newtonsoft.Json.Linq;

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

    /// <summary>
    /// Global connection timeout (Http mode only). Acts as the remote server's transport-layer
    /// connection timeout + MCP initialization timeout, with a hard fallback via the host-side CTS
    /// (SDK 2.x internal timeouts may fail for some remote servers — see csharp-sdk#784).
    /// Per-server override via <see cref="McpServerConfiguration.Options"/> (the <c>connectionTimeout</c> key).
    /// </summary>
    public McpScope WithConnectionTimeout(TimeSpan? timeout)
    {
        ConnectionTimeout = timeout;
        return this;
    }

    internal TimeSpan? ConnectionTimeout { get; private set; }

    // ── Global bindable status ─────────────────────────────────────────────

    /// <summary>
    /// Globally bindable server status view-model. The host UI binds <see cref="McpStatusViewModel.Servers"/> to show
    /// each server's alive/installing/connecting/error status. Driven live during <see cref="LoadAsync"/>.
    /// </summary>
    public McpStatusViewModel Status { get; } = new();

    // ── Loaded MCP server tools (dynamic, supports mid-session add/remove) ──

    private readonly object _loadedToolsLock = new();
    private readonly Dictionary<string, IReadOnlyList<AITool>> _loadedToolSets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All tools of currently connected MCP servers (aggregated per server). Once a server loads
    /// successfully mid-session (<see cref="LoadAsync"/>), its tools are immediately available; they
    /// disappear automatically after unloading (<see cref="UnloadServer"/>). Each Agent conversation
    /// assembles <c>ChatOptions.Tools</c> as "base tools + <see cref="LoadedTools"/>", so tools can be
    /// added/removed mid-session.
    /// </summary>
    public IReadOnlyList<AITool> LoadedTools
    {
        get
        {
            lock (_loadedToolsLock)
                return _loadedToolSets.Values.SelectMany(v => v).ToArray();
        }
    }

    /// <summary>
    /// Unloads a server (mid-session removal): removes its tool set and resets its status to
    /// <see cref="McpServerStatus.NotStarted"/>. Returns whether any loaded tools were present.
    /// </summary>
    public bool UnloadServer(string name)
    {
        bool removed;
        lock (_loadedToolsLock)
            removed = _loadedToolSets.Remove(name);

        UpdateStatus(() =>
        {
            var status = Status.Servers.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (status is not null)
                status.State = McpServerStatus.NotStarted;
        });
        return removed;
    }

    /// <summary>
    /// UI thread context (optional). When registered, all status updates marshal to this context,
    /// for hosts that bind on a UI thread (WPF/Avalonia, etc.); when not registered, updates run
    /// on the caller's thread.
    /// </summary>
    public McpScope WithSynchronizationContext(SynchronizationContext? context)
    {
        UIContext = context;
        return this;
    }

    internal SynchronizationContext? UIContext { get; private set; }

    /// <summary>Marshals a status update to the UI thread (when registered and not already on it).</summary>
    private void UpdateStatus(Action update)
    {
        var ui = UIContext;
        if (ui is null || ReferenceEquals(ui, SynchronizationContext.Current))
        {
            update();
            return;
        }
        ui.Post(_ => update(), null);
    }

    // ── Remote (Http) OAuth redirect ─────────────────────────────────────────

    private AuthorizationRedirectDelegate? _oauthAuthorizationRedirect;

    /// <summary>
    /// Registers the OAuth authorization-redirect handler for remote (<see cref="McpServerRunMode.Http"/>)
    /// servers. The handler receives the <paramref name="authorizationUri"/> to open in the user's browser
    /// and the expected <paramref name="redirectUri"/>, waits for authorization, and returns the final
    /// redirect URL carrying the auth code (as a string). When not set, the MCP SDK's default console-input
    /// handler is used (headless scenarios should always register this). Replaces any previously registered handler.
    /// </summary>
    public McpScope WithOAuthAuthorizationRedirect(Func<Uri, Uri, CancellationToken, Task<string?>> handler)
    {
        _oauthAuthorizationRedirect = handler is null ? null : new AuthorizationRedirectDelegate(handler);
        return this;
    }

    // ── Execution ──────────────────────────────────────────────────────────

    public async Task<AITool[]> LoadAsync(
        IEnumerable<McpServerConfiguration> servers, CancellationToken ct = default)
    {
        var mcpRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, McpRootRelative));
        Directory.CreateDirectory(mcpRoot);

        UpdateStatus(() => { Status.Reset(); Status.SetLoading(true); });
        lock (_loadedToolsLock)
            _loadedToolSets.Clear();
        try
        {
            var allTools = new List<AITool>();
            foreach (var config in servers)
            {
                if (config is null) continue;
                allTools.AddRange(await LoadOneAsync(config, mcpRoot, ct));
            }
            return [.. allTools];
        }
        finally
        {
            UpdateStatus(() => Status.SetLoading(false));
        }
    }

    private async Task<AITool[]> LoadOneAsync(McpServerConfiguration config, string mcpRoot, CancellationToken ct)
    {
        var status = TrackServer(config);
        try
        {
            // Local mode: first install/prepare the runtime (Installing), then connect (Connecting).
            if (config.RunMode is McpServerRunMode.Npm or McpServerRunMode.Pip)
            {
                SetServerState(status, McpServerStatus.Installing);
                if (config.RunMode == McpServerRunMode.Npm)
                    await EnsureNpmPackageAsync(config.Package, config.Version, mcpRoot, ct);
                else
                    await EnsurePipPackageAsync(config.Package, config.Version, mcpRoot, ct);
            }

            SetServerState(status, McpServerStatus.Connecting);
            var tools = await ConnectServerAsync(config, mcpRoot, ct);

            UpdateStatus(() =>
            {
                status.ToolCount = tools.Length;
                status.State = McpServerStatus.Connected;
            });
            lock (_loadedToolsLock)
                _loadedToolSets[config.Name] = tools;
            return tools;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UpdateStatus(() =>
            {
                status.Error = ex.Message;
                status.State = McpServerStatus.Error;
            });
            ServerError?.Invoke(config, ex);
            return [];
        }
    }

    /// <summary>The tools of a connected server (empty when not connected).</summary>
    public IReadOnlyList<AITool> GetServerTools(string name)
    {
        lock (_loadedToolsLock)
            return _loadedToolSets.TryGetValue(name, out var tools) ? tools : [];
    }

    // ── Status driving helpers ─────────────────────────────────────────────

    private McpServerStatusViewModel TrackServer(McpServerConfiguration config)
    {
        // When reloading a same-named server, update the existing status entry to avoid duplicates.
        var existing = Status.Servers.FirstOrDefault(s => string.Equals(s.Name, config.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            UpdateStatus(() =>
            {
                existing.Description = config.Description;
                existing.RunMode = config.RunMode;
                existing.Endpoint = config.Endpoint;
                existing.Error = null;
                existing.ToolCount = 0;
                existing.State = McpServerStatus.NotStarted;
            });
            return existing;
        }

        var status = new McpServerStatusViewModel
        {
            Name = config.Name,
            Description = config.Description,
            RunMode = config.RunMode,
            Endpoint = config.Endpoint,
        };
        UpdateStatus(() => Status.Track(status));
        return status;
    }

    private void SetServerState(McpServerStatusViewModel status, McpServerStatus state)
        => UpdateStatus(() => status.State = state);

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

            // Step 2: pip install into the venv
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

    private async Task<AITool[]> ConnectServerAsync(
        McpServerConfiguration config, string mcpRoot, CancellationToken ct)
    {
        var transport = config.RunMode == McpServerRunMode.Http
            ? (IClientTransport)CreateHttpTransport(config)
            : CreateStdioTransport(config, mcpRoot);

        var effectiveTimeout = GetEffectiveConnectionTimeout(config);

        // Host-side hard fallback: SDK 2.x internal timeouts may fail for some remote servers
        // (csharp-sdk#784). Use a linked CTS so connection/initialization can never hang forever.
        // Only when OUR timeout fires (not caller cancellation) wrap it as a TimeoutException and
        // hand it back to LoadAsync to be handled as a per-server error.
        using var timeoutCts = effectiveTimeout is { } t && config.RunMode == McpServerRunMode.Http
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        if (timeoutCts is not null)
            timeoutCts.CancelAfter(effectiveTimeout!.Value);

        var connectCt = timeoutCts?.Token ?? ct;
        var options = effectiveTimeout is { } to && config.RunMode == McpServerRunMode.Http
            ? new McpClientOptions { InitializationTimeout = to }
            : null;

        try
        {
            var client = await McpClient.CreateAsync(transport, options, null, connectCt);
            var tools = await client.ListToolsAsync();
            return [.. tools.Cast<AITool>()];
        }
        catch (OperationCanceledException) when (
            timeoutCts is { IsCancellationRequested: true } && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"MCP server '{config.Name}' connection timed out after {effectiveTimeout}.");
        }
    }

    private static IClientTransport CreateStdioTransport(McpServerConfiguration config, string mcpRoot)
        => new StdioClientTransport(BuildStdioTransportOptions(config, mcpRoot));

    /// <summary>Builds the stdio transport options: command line + Options (env / workingDirectory). Internal for tests.</summary>
    internal static StdioClientTransportOptions BuildStdioTransportOptions(McpServerConfiguration config, string mcpRoot)
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

        var stdioOptions = new StdioClientTransportOptions
        {
            Name = config.Name,
            Command = cmd,
            Arguments = [.. args],
        };

        var j = ParseOptions(config.Options);
        EnsureKnownKeys(j, StdioOptionKeys, config.Name);
        if (TryGetOption(j, "env", out var env))
            stdioOptions.EnvironmentVariables = env.ToObject<Dictionary<string, string?>>();
        if (TryGetOption(j, "workingDirectory", out var wd))
            stdioOptions.WorkingDirectory = wd.Value<string>();

        return stdioOptions;
    }

    /// <summary>
    /// Builds an HTTP (Streamable HTTP, SSE fallback) client transport for a remote
    /// <see cref="McpServerRunMode.Http"/> server. The 2-arg transport constructor owns its own
    /// <see cref="HttpClient"/>; extra headers come from <see cref="HttpClientTransportOptions.AdditionalHeaders"/>.
    /// </summary>
    internal HttpClientTransport CreateHttpTransport(McpServerConfiguration config)
        => new(BuildHttpTransportOptions(config), null);

    /// <summary>
    /// Builds the <see cref="HttpClientTransportOptions"/> for a remote server: endpoint (structural),
    /// plus the flexible <see cref="McpServerConfiguration.Options"/> blob — known keys map to
    /// <c>headers</c> / <c>oauth</c> / <c>connectionTimeout</c> / <c>transportMode</c> / <c>ownsSession</c>;
    /// unknown keys are rejected.
    /// </summary>
    internal HttpClientTransportOptions BuildHttpTransportOptions(McpServerConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new InvalidOperationException($"MCP Http run mode requires an Endpoint URL. Server '{config.Name}' has none.");

        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(config.Endpoint),
            Name = config.Name,
        };

        var j = ParseOptions(config.Options);
        EnsureKnownKeys(j, HttpOptionKeys, config.Name);

        if (TryGetOption(j, "headers", out var headersToken))
            options.AdditionalHeaders = headersToken.ToObject<Dictionary<string, string>>() ?? [];

        if (TryGetOption(j, "oauth", out var oauthToken) && oauthToken is JObject o)
        {
            var redirectUri = o["redirectUri"]?.Value<string>();
            options.OAuth = new ClientOAuthOptions
            {
                ClientId = o["clientId"]?.Value<string>() ?? string.Empty,
                ClientSecret = o["clientSecret"]?.Value<string>(),
                // RedirectUri is a `required` member of ClientOAuthOptions; fall back to a loopback default when the config omits it.
                RedirectUri = redirectUri is not null ? new Uri(redirectUri) : new Uri("http://localhost/oauth/callback"),
                Scopes = o["scopes"]?.ToObject<string[]>(),
                AuthorizationRedirectDelegate = _oauthAuthorizationRedirect,
            };
        }

        // Connection timeout: Options.connectionTimeout takes priority, otherwise the scope-wide WithConnectionTimeout.
        if (TryGetOption(j, "connectionTimeout", out var ct))
            options.ConnectionTimeout = ParseTimeSpan(ct);
        else if (ConnectionTimeout is { } globalTimeout)
            options.ConnectionTimeout = globalTimeout;
        if (TryGetOption(j, "transportMode", out var tm)
            && Enum.TryParse<HttpTransportMode>(tm.Value<string>(), ignoreCase: true, out var mode))
            options.TransportMode = mode;
        if (TryGetOption(j, "ownsSession", out var os))
            options.OwnsSession = os.Value<bool>();

        return options;
    }

    // ── McpServerConfiguration.Options (anonymous-object JSON blob) ────────

    private static readonly string[] HttpOptionKeys = ["headers", "oauth", "connectionTimeout", "transportMode", "ownsSession"];
    private static readonly string[] StdioOptionKeys = ["env", "workingDirectory"];

    /// <summary>Parses Options (an anonymous object or JSON string) into a JObject; null → an empty object.</summary>
    private static JObject ParseOptions(object? options)
    {
        if (options is null) return new JObject();
        JToken token = options is string s ? JToken.Parse(s) : JToken.FromObject(options);
        if (token is not JObject obj)
            throw new InvalidOperationException("McpServerConfiguration.Options must be an object (anonymous object), not a scalar or an array.");
        return obj;
    }

    private static bool TryGetOption(JObject j, string key, out JToken token)
    {
        if (j.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var value) && value.Type != JTokenType.Null)
        {
            token = value;
            return true;
        }
        token = JValue.CreateNull();
        return false;
    }

    private static void EnsureKnownKeys(JObject j, IEnumerable<string> allowed, string serverName)
    {
        var unknown = j.Properties()
            .Select(p => p.Name)
            .Where(n => !allowed.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"MCP server '{serverName}' has unknown Options key(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", allowed)}.");
    }

    private static TimeSpan ParseTimeSpan(JToken token)
    {
        if (token.Type is JTokenType.Integer or JTokenType.Float)
            return TimeSpan.FromSeconds(token.Value<double>());
        var s = token.Value<string>();
        if (s is not null && TimeSpan.TryParse(s, out var ts))
            return ts;
        throw new InvalidOperationException($"Invalid connectionTimeout value: {token}.");
    }

    /// <summary>Per-server connection timeout: Options.connectionTimeout (seconds or a TimeSpan string) overrides the global value.</summary>
    internal TimeSpan? GetEffectiveConnectionTimeout(McpServerConfiguration config)
    {
        var j = ParseOptions(config.Options);
        return TryGetOption(j, "connectionTimeout", out var ct) ? ParseTimeSpan(ct) : ConnectionTimeout;
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
