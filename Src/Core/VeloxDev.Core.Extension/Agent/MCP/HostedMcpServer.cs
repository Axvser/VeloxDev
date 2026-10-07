using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.MCP;

/// <summary>
/// One MCP server as the scope hosts it: the client it is currently connected through, the tools it offers, and
/// the lock that serialises a rebuild.
/// </summary>
/// <remarks>
/// <para>
/// The server is <b>hosted</b>, not merely cached: the tools the Agent holds are <see cref="McpToolProxy"/>
/// instances this type owns, so their identity outlives the connection. When the configuration moves — a new
/// allowed directory for a filesystem server, say — the next call connects with the new configuration and
/// <b>only then</b> releases the previous client. A configuration that cannot be reached costs nothing: the
/// working connection keeps serving and the failure is reported instead.
/// </para>
/// <para>
/// The lock is per server. Two servers rebuild independently, and only calls that arrive while a rebuild is owed
/// queue behind it; a call that finds the connection current takes no lock at all.
/// </para>
/// <para>
/// <b>Every report is raised after the lock is released.</b> That ordering is load-bearing: the scope takes its
/// own lock to mutate its server map and calls in here from under it, so calling back out while holding this lock
/// would close the cycle.
/// </para>
/// </remarks>
internal sealed class HostedMcpServer
{
    /// <summary>Connects a configuration and returns the client with the tools it offers.</summary>
    /// <remarks>The installation root is bound in by the scope, which resolves it per load.</remarks>
    internal delegate Task<McpConnection> Connector(McpServerConfiguration config, CancellationToken ct);

    /// <summary>What this server reports back to the scope. Raised outside the lock.</summary>
    internal enum Phase
    {
        /// <summary>A rebuild is starting. The previous connection is still serving.</summary>
        Connecting,

        /// <summary>A rebuild finished. Whether anything the model sees changed is the scope's to decide.</summary>
        Refreshed,

        /// <summary>A rebuild or a release failed. A previous connection, if there was one, is still serving.</summary>
        Failed,
    }

    private readonly string _name;
    private readonly Connector _connect;
    private readonly Action<Phase, McpServerConfiguration, Exception?> _report;

    /// <summary>The async lock: one per server, held across a rebuild and across a release.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Guarded by _gate. Whether a rebuild is owed is decided outside it, so those two counters live with the
    // desired state instead.
    private IAsyncDisposable? _client;
    private Dictionary<string, AIFunction>? _live;
    private readonly Dictionary<string, McpToolProxy> _proxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IAsyncDisposable> _retired = [];

    // Guarded by _desiredSync: what the host wants, and how far that has been carried out.
    private readonly object _desiredSync = new();
    private McpServerConfiguration _desired;
    private long _revision;
    private long _builtFor = -1;
    private long _attemptedFor = -1;

    // Published so a read never queues behind a connect.
    private readonly object _publishedSync = new();
    private IReadOnlyList<AITool> _offered = [];
    private IReadOnlyDictionary<string, ToolShape> _shapes = new Dictionary<string, ToolShape>();
    private Dictionary<string, AIFunction>? _liveForInvoke;

    private int _inFlight;

    internal HostedMcpServer(
        string name,
        McpServerConfiguration config,
        Connector connect,
        Action<Phase, McpServerConfiguration, Exception?> report)
    {
        _name = name;
        _desired = config;
        _connect = connect;
        _report = report;
    }

    /// <summary>The server's name, as it appears in the status list.</summary>
    internal string Name => _name;

    /// <summary>The configuration this server is being pointed at, wanted or already reached.</summary>
    internal McpServerConfiguration Configuration
    {
        get { lock (_desiredSync) return _desired; }
    }

    /// <summary>The tools currently offered, as stable proxies. Read without taking the lock.</summary>
    internal IReadOnlyList<AITool> OfferedTools
    {
        get { lock (_publishedSync) return _offered; }
    }

    /// <summary>Points the server at a new configuration. Marks the connection stale; releases nothing yet.</summary>
    internal void SetConfiguration(McpServerConfiguration config)
    {
        lock (_desiredSync)
        {
            _desired = config;
            _revision++;
        }
    }

    /// <summary>
    /// Connects for the first time and publishes the tools. The scope drives the status around this call, because
    /// a first load has phases — installing a runtime, for one — that a rebuild does not.
    /// </summary>
    internal async Task<IReadOnlyList<AITool>> StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long revision;
            McpServerConfiguration config;
            lock (_desiredSync)
            {
                revision = _revision;
                config = _desired;
            }

            Adopt(await _connect(config, ct).ConfigureAwait(false), revision);
            Reconcile();
            return OfferedTools;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Connects now, because the host asked for it. Unlike the lazy path this always tries, even if that same
    /// configuration was already attempted and refused — an explicit load is a fresh instruction, not a retry.
    /// </summary>
    /// <returns>Whether the new configuration was reached. When it was not, what the server offers is unchanged.</returns>
    internal async Task<bool> ReloadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await EnsureCurrentAsync(ct, force: true).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Invokes one tool by name, rebuilding first when the configuration has moved on.</summary>
    /// <param name="toolName">The name the proxy was created with.</param>
    /// <param name="arguments">The arguments, passed through to the live tool unchanged.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The live tool's result, or an error envelope when the server no longer offers that tool.</returns>
    internal async ValueTask<object?> InvokeAsync(string toolName, AIFunctionArguments arguments, CancellationToken ct)
    {
        if (RebuildOwed())
        {
            var rebuilt = false;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                rebuilt = await EnsureCurrentAsync(ct, force: false).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            // Outside the lock: the scope takes its own while re-reading what this server offers.
            if (rebuilt) _report(Phase.Refreshed, Configuration, null);
        }

        var live = Volatile.Read(ref _liveForInvoke);
        if (live is null || !live.TryGetValue(toolName, out var tool))
            return Error($"Server '{_name}' no longer offers a tool named '{toolName}' — it was rebuilt from a new "
                         + "configuration. Use the tools it offers now.");

        Interlocked.Increment(ref _inFlight);
        try
        {
            return await tool.InvokeAsync(arguments, ct).ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlight) == 0) await ReleaseRetiredAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases everything this server holds. Waits for a rebuild in flight; does not wait for calls, which is
    /// what unloading has always done.
    /// </summary>
    internal async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var retired in _retired) await TryReleaseAsync(retired).ConfigureAwait(false);
            _retired.Clear();
            if (_client is not null) await TryReleaseAsync(_client).ConfigureAwait(false);

            _client = null;
            _live = null;
            Volatile.Write(ref _liveForInvoke, null);
            _proxies.Clear();

            lock (_desiredSync) _builtFor = -1;
            Publish([], new Dictionary<string, ToolShape>());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The description and schema a proxy reports. Read without the gate — a render must not queue.</summary>
    internal ToolShape ShapeOf(string toolName)
    {
        lock (_publishedSync)
            return _shapes.TryGetValue(toolName, out var shape) ? shape : ToolShape.Empty;
    }

    // ── Internals ───────────────────────────────────────────────────────────

    /// <summary>
    /// Whether a call should do the work. One configuration is attempted once, not once per call: a server whose
    /// new configuration cannot be reached must not turn every later call into another connect.
    /// </summary>
    private bool RebuildOwed()
    {
        lock (_desiredSync)
            return _builtFor >= 0 && _builtFor != _revision && _attemptedFor != _revision;
    }

    /// <summary>Returns whether a connect was made and succeeded. Reports the failure itself when it did not.</summary>
    private async Task<bool> EnsureCurrentAsync(CancellationToken ct, bool force)
    {
        long revision;
        McpServerConfiguration config;
        lock (_desiredSync)
        {
            // A caller that queued behind the one that did the work finds nothing owed here.
            if (!force && (_builtFor == _revision || _attemptedFor == _revision)) return false;
            revision = _revision;
            config = _desired;
        }

        _report(Phase.Connecting, config, null);

        McpConnection connection;
        try
        {
            // Connect first. A configuration that cannot be reached must not cost the working connection.
            connection = await _connect(config, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Marked attempted only once it has actually been tried, so a cancelled attempt is still retried.
            lock (_desiredSync) _attemptedFor = revision;
            _report(Phase.Failed, config, ex);
            return false;
        }

        var replaced = _client;
        Adopt(connection, revision);
        lock (_desiredSync) _attemptedFor = revision;

        // Retired rather than released: a call that began before this rebuild may still be inside the old client.
        if (replaced is not null)
        {
            _retired.Add(replaced);
            if (Volatile.Read(ref _inFlight) == 0) await ReleaseRetiredLockedAsync().ConfigureAwait(false);
        }

        Reconcile();
        return true;
    }

    private void Adopt(McpConnection connection, long revision)
    {
        _client = connection.Client;
        _live = connection.Tools.ToDictionary(static t => t.Name, StringComparer.OrdinalIgnoreCase);
        Volatile.Write(ref _liveForInvoke, _live);
        lock (_desiredSync) _builtFor = revision;
    }

    /// <summary>
    /// Brings the published proxies in line with the live tools. A name that already has a proxy keeps that
    /// instance — that stability is the point of hosting the server here.
    /// </summary>
    private void Reconcile()
    {
        var live = _live ?? [];
        var shapes = new Dictionary<string, ToolShape>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in live)
        {
            // Not deconstructed: KeyValuePair.Deconstruct is not in netstandard2.0.
            if (!_proxies.ContainsKey(entry.Key)) _proxies[entry.Key] = new McpToolProxy(entry.Key, this);

            // Cloned: a schema must outlive the connection it came from, and an uncloned JsonElement is only
            // valid while the document behind it lives.
            shapes[entry.Key] = new ToolShape(entry.Value.Description, entry.Value.JsonSchema.Clone());
        }

        Publish([.. live.Keys.Select(name => (AITool)_proxies[name])], shapes);
    }

    private void Publish(IReadOnlyList<AITool> offered, IReadOnlyDictionary<string, ToolShape> shapes)
    {
        lock (_publishedSync)
        {
            _offered = offered;
            _shapes = shapes;
        }
    }

    /// <summary>Releases retired clients once nothing is inside them. Called with the gate already held.</summary>
    private async Task ReleaseRetiredLockedAsync()
    {
        if (Volatile.Read(ref _inFlight) != 0) return;

        foreach (var retired in _retired) await TryReleaseAsync(retired).ConfigureAwait(false);
        _retired.Clear();
    }

    private async Task ReleaseRetiredAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReleaseRetiredLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TryReleaseAsync(IAsyncDisposable client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A client that will not release is reported, never thrown: this runs on the path of somebody's call.
            _report(Phase.Failed, Configuration, ex);
        }
    }

    private static string Error(string message)
        => $"{{\"status\":\"error\",\"message\":{JsonSerializer.Serialize(message)}}}";

    /// <summary>What a proxy reports for the tool behind it.</summary>
    internal readonly record struct ToolShape(string Description, JsonElement Schema)
    {
        internal static readonly ToolShape Empty = new(string.Empty, default);
    }
}

/// <summary>One connection: the client that owns it, and the tools it offers, by name.</summary>
/// <remarks>
/// The client is held as <see cref="IAsyncDisposable"/> rather than as <c>McpClient</c> on purpose — releasing it
/// is the whole of what the scope does with it, and holding it that way lets a test supply a connection without
/// standing up a real client.
/// </remarks>
internal sealed record McpConnection(IAsyncDisposable Client, IReadOnlyList<AIFunction> Tools);
