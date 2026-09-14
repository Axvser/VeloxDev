using CliWrap;
using CliWrap.Buffered;
using Microsoft.Agents.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
    /// How far the Agent may go in adding MCP servers of its own. Defaults to
    /// <see cref="McpSelfServiceLevel.Closed"/>, under which the add tool does not exist.
    /// </summary>
    public McpSelfServiceLevel SelfServiceLevel { get; private set; } = McpSelfServiceLevel.Closed;

    /// <summary>
    /// Opens the self-service ladder to <paramref name="level"/>. Read the level's documentation before
    /// raising it: at <see cref="McpSelfServiceLevel.AllConfirmed"/> and above the Agent can cause a
    /// package to be installed and launched on this machine.
    /// </summary>
    public McpScope WithSelfService(McpSelfServiceLevel level)
    {
        if (SelfServiceLevel == level) return this;
        SelfServiceLevel = level;
        // The level decides whether AddMcpServer exists and what the prompt says the model may do with
        // it, so a cached render keyed on Version has to be invalidated here.
        Interlocked.Increment(ref _version);
        return this;
    }

    /// <summary>
    /// Asks the user to approve an operation the Agent requested. Returns <c>true</c> to proceed.
    /// Registered on this scope so MCP self-service can gate itself without depending on the workflow
    /// layer; <c>WorkflowAgentScope.WithMcps</c> wires this to the scope's own confirmation handler.
    /// When no handler is registered the answer is <c>false</c> — an unanswerable prompt must deny, not
    /// silently allow.
    /// </summary>
    public McpScope WithConfirmationHandler(Func<string, string, Task<bool>> handler)
    {
        _confirmationHandler = handler;
        return this;
    }

    private Func<string, string, Task<bool>>? _confirmationHandler;

    /// <summary>Runs the registered confirmation handler, denying when there is none.</summary>
    internal Func<string, string, Task<bool>> ConfirmationResolver =>
        _confirmationHandler ?? ((_, _) => Task.FromResult(false));

    /// <summary>
    /// Whether a server of this run mode may be added by the Agent at the current
    /// <see cref="SelfServiceLevel"/>.
    /// </summary>
    public bool CanAddServer(McpServerRunMode runMode)
    {
        if (SelfServiceLevel == McpSelfServiceLevel.Closed) return false;

        var isLocal = runMode != McpServerRunMode.Http;
        if (!isLocal) return true;

        // Local modes install and launch a package, so they open one rung later than remote ones.
        return SelfServiceLevel >= McpSelfServiceLevel.AllConfirmed;
    }

    /// <summary>Whether adding a server at this level requires the user's agreement.</summary>
    public bool RequiresConfirmationToAdd()
        => SelfServiceLevel is McpSelfServiceLevel.RemoteConfirmed or McpSelfServiceLevel.AllConfirmed;

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
    /// Live clients of currently connected servers, keyed the same way as <see cref="_loadedToolSets"/>.
    /// A client MUST be retained for as long as its tools are offered: every <c>McpClientTool</c> holds a
    /// reference to its owning <see cref="McpClient"/>, and for stdio modes that client owns the child
    /// process. Dropping the reference without disposing leaks the process.
    /// </summary>
    private readonly Dictionary<string, McpClient> _loadedClients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Configurations of currently connected servers, so teardown failures can be reported against the
    /// server they belong to instead of a name alone.
    /// </summary>
    private readonly Dictionary<string, McpServerConfiguration> _loadedConfigs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All tools of currently connected MCP servers (aggregated per server). Once a server loads
    /// successfully mid-session (<see cref="LoadAsync"/> / <see cref="AddAsync"/>), its tools are
    /// immediately available; they disappear automatically after unloading
    /// (<see cref="UnloadServer"/> / <see cref="UnloadServerAsync"/>).
    /// </summary>
    public IReadOnlyList<AITool> LoadedTools
    {
        get
        {
            lock (_loadedToolsLock)
                return _loadedToolSets.Values.SelectMany(v => v).ToArray();
        }
    }

    private long _version;

    /// <summary>
    /// Monotonic version of the loaded-server set. Advances whenever a server is loaded, added or
    /// unloaded, so a prompt provider caching on it knows when its tool list went stale.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Identifies this scope for a context provider's session-state key. The Agent Framework throws when
    /// two providers attached to one agent share a key, and keys default to the provider's type name — so
    /// the discriminator has to live on the scope, not on the provider.
    /// <para>
    /// Per scope, deliberately: two providers built from <i>one</i> scope then collide and fail loudly at
    /// agent construction, which is the right outcome. A per-provider id would instead let both through
    /// and duplicate every MCP tool and the inventory block in the same turn.
    /// </para>
    /// </summary>
    internal string InstanceId { get; } = Guid.NewGuid().ToString("N");

    // ── Registered configurations ───────────────────────────────────────────

    private readonly object _registeredLock = new();
    private readonly List<McpServerConfiguration> _registered = [];

    /// <summary>
    /// The configurations the Agent may load by name. Populated by <see cref="WithServers"/>, and also by
    /// whatever <see cref="LoadAsync"/> or <see cref="AddAsync"/> was actually given — so a host that
    /// only loads directly still gets a reloadable set rather than an empty one.
    /// </summary>
    public IReadOnlyList<McpServerConfiguration> RegisteredServers
    {
        get
        {
            lock (_registeredLock)
                return [.. _registered];
        }
    }

    /// <summary>
    /// Pre-registers configurations for the Agent to load by name. A later call with the same name
    /// replaces the earlier one.
    /// </summary>
    public McpScope WithServers(params McpServerConfiguration[] servers)
    {
        var changed = false;
        foreach (var config in servers ?? [])
        {
            if (config is null) continue;
            Remember(config);
            changed = true;
        }
        // What the Agent may load by name is part of what it is told, so a cached render is stale now.
        if (changed) Interlocked.Increment(ref _version);
        return this;
    }

    /// <summary>Records a configuration under its name, replacing any earlier one with that name.</summary>
    private void Remember(McpServerConfiguration config)
    {
        lock (_registeredLock)
        {
            _registered.RemoveAll(c => string.Equals(c.Name, config.Name, StringComparison.OrdinalIgnoreCase));
            _registered.Add(config);
        }
    }

    /// <summary>
    /// Makes a server's tools available without connecting to anything. Internal for tests.
    /// <para>
    /// The loaded-server path is the one a composing host is most likely to duplicate, and it is otherwise
    /// unreachable without a live MCP server or a fake transport — so without this seam the tests that
    /// guard it would assert against an empty set and pass no matter what.
    /// </para>
    /// </summary>
    /// <param name="name">Server name, as it would appear in the status list.</param>
    /// <param name="tools">The tools to expose as that server's.</param>
    internal void SeedLoadedTools(string name, IEnumerable<AITool> tools)
    {
        lock (_loadedToolsLock)
            _loadedToolSets[name] = [.. tools];

        UpdateStatus(() => Status.Track(new McpServerStatusViewModel
        {
            Name = name,
            State = McpServerStatus.Connected,
            ToolCount = _loadedToolSets[name].Count,
        }));

        Interlocked.Increment(ref _version);
    }

    /// <summary>
    /// What the model is told about the currently connected servers, as a Markdown table. Empty when no
    /// server has been registered.
    /// <para>
    /// Reads the status snapshot rather than the bound collection: a prompt is rendered on the agent
    /// invocation thread, not the UI thread.
    /// </para>
    /// </summary>
    public string BuildInventoryBlock()
    {
        var servers = Status.Snapshot;
        if (servers.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Connected MCP servers");
        sb.AppendLine();
        sb.AppendLine("| Server | State | Tools |");
        sb.AppendLine("| ------ | ----- | ----- |");
        foreach (var server in servers)
            sb.AppendLine($"| {server.Name} | {server.StateText} | {server.ToolCount} |");
        return sb.ToString();
    }

    /// <summary>
    /// Creates the context provider that contributes this scope's server inventory, its management tools
    /// and every connected server's tools on each agent invocation — everything a host needs to use MCP
    /// without the workflow layer.
    /// </summary>
    /// <param name="policy">
    /// How the contributed tools behave. Omit for standalone use: the provider then builds a thread-only
    /// policy from this scope's own <see cref="WithSynchronizationContext"/>. A composing host passes
    /// <i>its</i> policy instead, so its budgets, callbacks and side effects still apply — and must pass
    /// the same instance it gives every other source, or the call counts diverge.
    /// </param>
    public AIContextProvider CreateContextProvider(AgentToolPolicy? policy = null)
        => new McpAgentContextProvider(this, policy);

    /// <summary>
    /// Unloads a server (mid-session removal): removes its tool set, resets its status to
    /// <see cref="McpServerStatus.NotStarted"/>, and disposes the underlying client (terminating the
    /// child process for stdio modes). Returns whether any loaded tools were present.
    /// <para>
    /// Blocks until disposal completes. Prefer <see cref="UnloadServerAsync"/> in async code — this
    /// overload exists for the synchronous callers the public surface already had.
    /// </para>
    /// </summary>
    public bool UnloadServer(string name)
        => UnloadServerAsync(name).GetAwaiter().GetResult();

    /// <summary>
    /// Unloads a server (mid-session removal): removes its tool set, resets its status to
    /// <see cref="McpServerStatus.NotStarted"/>, and disposes the underlying client. Returns whether any
    /// loaded tools were present. The server can be loaded again later via
    /// <see cref="LoadAsync"/> or <see cref="AddAsync"/>.
    /// </summary>
    public async Task<bool> UnloadServerAsync(string name)
    {
        McpClient? client;
        bool removed;
        lock (_loadedToolsLock)
        {
            removed = _loadedToolSets.Remove(name);
            if (!_loadedClients.TryGetValue(name, out client)) client = null;
            _loadedClients.Remove(name);
        }

        await RunOnUIAsync(() =>
        {
            var status = Status.Servers.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (status is not null)
                status.State = McpServerStatus.NotStarted;
        }).ConfigureAwait(false);

        if (client is not null)
            await client.DisposeAsync().ConfigureAwait(false);

        Interlocked.Increment(ref _version);
        return removed;
    }

    /// <summary>
    /// Disposes every connected server's client. For stdio modes this terminates the child processes,
    /// which would otherwise outlive the scope. Safe to call more than once.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var failures = await ReleaseAsync(DetachAllClients()).ConfigureAwait(false);
        lock (_loadedToolsLock)
            _loadedToolSets.Clear();

        // Unlike the replace path, explicit disposal surfaces teardown failures to its caller.
        if (failures.Count > 0)
            throw new AggregateException("One or more MCP clients failed to dispose.", failures);
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

    /// <summary>
    /// Marshals a status update to the UI thread and <b>awaits it</b>, so a caller that has just
    /// completed a load step observes the status it caused. <see cref="UpdateStatus"/> posts
    /// asynchronously, which leaves an ordering gap for anything awaited afterwards.
    /// <para>
    /// Awaiting (rather than <c>Send</c>-ing) is deliberate: the caller here is off the UI thread and the
    /// UI thread may itself be awaiting this operation, so a blocking send would deadlock.
    /// </para>
    /// </summary>
    private Task RunOnUIAsync(Action update)
    {
        var ui = UIContext;
        if (ui is null || ReferenceEquals(ui, SynchronizationContext.Current))
        {
            update();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(_ =>
        {
            try
            {
                update();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return tcs.Task;
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
        var mcpRoot = ResolveMcpRoot();

        await RunOnUIAsync(() => { Status.Reset(); Status.SetLoading(true); }).ConfigureAwait(false);
        // REPLACE semantics: whatever was loaded before is going away, so its clients must be released
        // here — otherwise the stdio child processes of the replaced servers outlive the reload.
        await ReleaseAsync(DetachAllClients()).ConfigureAwait(false);
        lock (_loadedToolsLock)
            _loadedToolSets.Clear();
        try
        {
            var allTools = new List<AITool>();
            foreach (var config in servers)
            {
                if (config is null) continue;
                // Loading a configuration makes it reloadable by name afterwards, so the Agent's
                // management tools can bring it back without the host handing the list over again.
                Remember(config);
                allTools.AddRange(await LoadOneAsync(config, mcpRoot, ct).ConfigureAwait(false));
            }
            return [.. allTools];
        }
        finally
        {
            await RunOnUIAsync(() => Status.SetLoading(false)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Loads <b>one</b> server without disturbing any server that is already loaded: the others keep
    /// their tools, their status entries and their live clients. This is the incremental counterpart to
    /// the destructive <see cref="LoadAsync"/>, and the entry point for adding a server mid-session.
    /// Returns the server's tools (empty when it failed — the failure is recorded on its status and
    /// raised through <see cref="ServerError"/>).
    /// </summary>
    public async Task<AITool[]> AddAsync(McpServerConfiguration config, CancellationToken ct = default)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        var mcpRoot = ResolveMcpRoot();
        Remember(config);

        await RunOnUIAsync(() => Status.SetLoading(true)).ConfigureAwait(false);
        try
        {
            return await LoadOneAsync(config, mcpRoot, ct).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUIAsync(() => Status.SetLoading(false)).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves and ensures the MCP installation root.</summary>
    private string ResolveMcpRoot()
    {
        var mcpRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, McpRootRelative));
        Directory.CreateDirectory(mcpRoot);
        return mcpRoot;
    }

    /// <summary>Detaches every loaded client (and its configuration) so it can be released.</summary>
    private (McpServerConfiguration? Config, McpClient Client)[] DetachAllClients()
    {
        lock (_loadedToolsLock)
        {
            var live = _loadedClients
                .Select(kvp => (
                    Config: _loadedConfigs.TryGetValue(kvp.Key, out var cfg) ? cfg : null,
                    Client: kvp.Value))
                .ToArray();
            _loadedClients.Clear();
            _loadedConfigs.Clear();
            return live;
        }
    }

    /// <summary>
    /// Releases clients, reporting each failure against its own server and never letting one
    /// unresponsive server strand the rest. Returns the failures for callers that must surface them.
    /// </summary>
    private async Task<List<Exception>> ReleaseAsync((McpServerConfiguration? Config, McpClient Client)[] live)
    {
        var failures = new List<Exception>();
        foreach (var (config, client) in live)
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (config is not null) ServerError?.Invoke(config, ex);
                failures.Add(ex);
            }
        }
        return failures;
    }

    private async Task<AITool[]> LoadOneAsync(McpServerConfiguration config, string mcpRoot, CancellationToken ct)
    {
        var status = await TrackServerAsync(config).ConfigureAwait(false);
        try
        {
            // Local mode: first install/prepare the runtime (Installing), then connect (Connecting).
            if (config.RunMode is McpServerRunMode.Npm or McpServerRunMode.Pip)
            {
                await SetServerStateAsync(status, McpServerStatus.Installing).ConfigureAwait(false);
                if (config.RunMode == McpServerRunMode.Npm)
                    await EnsureNpmPackageAsync(config.Package, config.Version, mcpRoot, ct).ConfigureAwait(false);
                else
                    await EnsurePipPackageAsync(config.Package, config.Version, mcpRoot, ct).ConfigureAwait(false);
            }

            await SetServerStateAsync(status, McpServerStatus.Connecting).ConfigureAwait(false);
            var (client, tools) = await ConnectServerAsync(config, mcpRoot, ct).ConfigureAwait(false);

            await RunOnUIAsync(() =>
            {
                status.ToolCount = tools.Length;
                status.State = McpServerStatus.Connected;
            }).ConfigureAwait(false);
            lock (_loadedToolsLock)
            {
                _loadedToolSets[config.Name] = tools;
                _loadedClients[config.Name] = client;
                _loadedConfigs[config.Name] = config;
            }
            Interlocked.Increment(ref _version);
            return tools;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RunOnUIAsync(() =>
            {
                status.Error = ex.Message;
                status.State = McpServerStatus.Error;
            }).ConfigureAwait(false);
            ServerError?.Invoke(config, ex);
            // A failed server changes observable state (a new status entry in Error) even though it
            // contributes no tools, so caches keyed on Version must still be invalidated.
            Interlocked.Increment(ref _version);
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

    /// <summary>
    /// Ensures a status entry exists for <paramref name="config"/> and returns it, reusing a same-named
    /// entry rather than duplicating it. The lookup and the mutation both run inside the marshalled
    /// block: <see cref="McpStatusViewModel.Servers"/> is an <c>ObservableCollection</c> bound to the
    /// host UI, so reading it from the loading thread is a cross-thread access.
    /// </summary>
    private async Task<McpServerStatusViewModel> TrackServerAsync(McpServerConfiguration config)
    {
        McpServerStatusViewModel? status = null;
        await RunOnUIAsync(() =>
        {
            var existing = Status.Servers.FirstOrDefault(
                s => string.Equals(s.Name, config.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Description = config.Description;
                existing.RunMode = config.RunMode;
                existing.Endpoint = config.Endpoint;
                existing.Error = null;
                existing.ToolCount = 0;
                existing.State = McpServerStatus.NotStarted;
                status = existing;
                return;
            }

            var created = new McpServerStatusViewModel
            {
                Name = config.Name,
                Description = config.Description,
                RunMode = config.RunMode,
                Endpoint = config.Endpoint,
            };
            Status.Track(created);
            status = created;
        }).ConfigureAwait(false);

        return status!;
    }

    private Task SetServerStateAsync(McpServerStatusViewModel status, McpServerStatus state)
        => RunOnUIAsync(() => status.State = state);

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

    private async Task<(McpClient Client, AITool[] Tools)> ConnectServerAsync(
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

        var client = await McpClient.CreateAsync(transport, options, null, connectCt).ConfigureAwait(false);
        try
        {
            var tools = await client.ListToolsAsync().ConfigureAwait(false);
            return (client, [.. tools.Cast<AITool>()]);
        }
        catch (OperationCanceledException) when (
            timeoutCts is { IsCancellationRequested: true } && !ct.IsCancellationRequested)
        {
            await SafeDisposeAsync(client).ConfigureAwait(false);
            throw new TimeoutException(
                $"MCP server '{config.Name}' connection timed out after {effectiveTimeout}.");
        }
        catch
        {
            // Listing failed after the client (and, for stdio, its child process) already exists. The
            // caller never gets a reference in this path, so it must be released here.
            await SafeDisposeAsync(client).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Disposes during a failure path, where a teardown error must not mask the original one.</summary>
    private static async Task SafeDisposeAsync(McpClient client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Intentionally ignored: the caller is already unwinding a more informative failure.
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
