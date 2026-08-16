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
    /// 全局连接超时（仅 Http 模式）。作为远程服务器的传输层连接超时 + MCP 初始化超时，并由
    /// 宿主侧 CTS 硬兜底（SDK 2.x 对部分远程服务器的内部超时可能失灵——见 csharp-sdk#784）。
    /// 逐服务器可用 <see cref="McpServerConfiguration.Options"/>（<c>connectionTimeout</c> 键）覆盖。
    /// </summary>
    public McpScope WithConnectionTimeout(TimeSpan? timeout)
    {
        ConnectionTimeout = timeout;
        return this;
    }

    internal TimeSpan? ConnectionTimeout { get; private set; }

    // ── Global bindable status ─────────────────────────────────────────────

    /// <summary>
    /// 全局可绑定的服务器状态视图模型。宿主 UI 绑定 <see cref="McpStatusViewModel.Servers"/> 以展示
    /// 每个服务器的存活/安装中/连接中/错误状态。<see cref="LoadAsync"/> 过程中实时驱动。
    /// </summary>
    public McpStatusViewModel Status { get; } = new();

    // ── 已加载的 MCP 服务器工具（动态，供中途增删）────────────────────────

    private readonly object _loadedToolsLock = new();
    private readonly Dictionary<string, IReadOnlyList<AITool>> _loadedToolSets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前已连接 MCP 服务器的全部工具（按服务器聚合）。服务器中途加载成功（<see cref="LoadAsync"/>）
    /// 后其工具即刻可用；卸载（<see cref="UnloadServer"/>）后自动消失。Agent 每次对话以
    /// “基础工具 + <see cref="LoadedTools"/>”组装 <c>ChatOptions.Tools</c>，即可中途加/移除工具。
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
    /// 卸载一个服务器（中途移除）：移除其工具集，并把状态置为 <see cref="McpServerStatus.NotStarted"/>。
    /// 返回是否确实存在已加载的工具。
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
    /// UI 线程上下文（可选）。注册后所有状态更新会 marshal 到该上下文，供 WPF/Avalonia 等
    /// UI 线程绑定的宿主使用；未注册时在调用方线程更新。
    /// </summary>
    public McpScope WithSynchronizationContext(SynchronizationContext? context)
    {
        UIContext = context;
        return this;
    }

    internal SynchronizationContext? UIContext { get; private set; }

    /// <summary>把状态更新 marshal 到 UI 线程（若已注册且当前不在该线程上）。</summary>
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
    public McpScope WithOAuthAuthorizationRedirect(Func<Uri, Uri, CancellationToken, Task<string>> handler)
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
            // 本地模式：先安装/准备运行时（Installing），再连接（Connecting）。
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

    /// <summary>某个已连接服务器的工具（未连接返回空）。</summary>
    public IReadOnlyList<AITool> GetServerTools(string name)
    {
        lock (_loadedToolsLock)
            return _loadedToolSets.TryGetValue(name, out var tools) ? tools : [];
    }

    // ── Status driving helpers ─────────────────────────────────────────────

    private McpServerStatusViewModel TrackServer(McpServerConfiguration config)
    {
        // 重载同名单服务器时更新既有状态条目，避免重复添加。
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

    private async Task<AITool[]> ConnectServerAsync(
        McpServerConfiguration config, string mcpRoot, CancellationToken ct)
    {
        var transport = config.RunMode == McpServerRunMode.Http
            ? (IClientTransport)CreateHttpTransport(config)
            : CreateStdioTransport(config, mcpRoot);

        var effectiveTimeout = GetEffectiveConnectionTimeout(config);

        // 宿主侧硬兜底：SDK 2.x 对部分远程服务器的内部超时可能失灵（csharp-sdk#784），
        // 用 linked CTS 保证连接/初始化不无限挂起。仅当是我们自己的超时触发（而非调用方
        // 取消）时，包装成 TimeoutException 交回 LoadAsync 按服务器错误处理。
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

    /// <summary>构建 stdio transport 选项：命令行 + Options（env / workingDirectory）。internal 供测试。</summary>
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
            stdioOptions.EnvironmentVariables = env.ToObject<Dictionary<string, string>>();
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
            options.OAuth = new ClientOAuthOptions
            {
                ClientId = o["clientId"]?.Value<string>() ?? string.Empty,
                ClientSecret = o["clientSecret"]?.Value<string>(),
                RedirectUri = o["redirectUri"]?.Value<string>() is { } ru ? new Uri(ru) : null,
                Scopes = o["scopes"]?.ToObject<string[]>(),
                AuthorizationRedirectDelegate = _oauthAuthorizationRedirect,
            };
        }

        // 连接超时：Options.connectionTimeout 优先，否则用 scope 全局 WithConnectionTimeout。
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

    /// <summary>解析 Options（匿名对象或 JSON 字符串）为 JObject；null → 空对象。</summary>
    private static JObject ParseOptions(object? options)
    {
        if (options is null) return new JObject();
        JToken token = options is string s ? JToken.Parse(s) : JToken.FromObject(options);
        if (token is not JObject obj)
            throw new InvalidOperationException("McpServerConfiguration.Options 必须是对象（匿名对象），不能是标量或数组。");
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

    /// <summary>逐服务器的连接超时：Options.connectionTimeout（秒或 TimeSpan 字符串）覆盖全局。</summary>
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
