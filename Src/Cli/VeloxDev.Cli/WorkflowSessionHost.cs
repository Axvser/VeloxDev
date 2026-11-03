using Microsoft.Extensions.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Cli;

/// <summary>
/// Owns the current <see cref="WorkflowSession"/> and lets it be replaced without restarting the server.
/// <para>
/// Native tool calls hold a lease on one session, so a reload never disposes a session a call is still using: the
/// old one is retired and disposed when its last lease is released. The shared <see cref="PluginLoader"/> and log
/// writer outlive sessions, which is what makes a reload cheap and keeps one log file across archives.
/// </para>
/// <para>
/// Reload is refused in <c>--in-process-execution</c> mode: the toolkit's background run handles belong to the
/// session being replaced, and there is no safe way to carry them across a swap. Isolated runs are snapshots owned
/// by child processes, so they are unaffected.
/// </para>
/// </summary>
internal sealed class WorkflowSessionHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly CliOptions _options;
    private readonly PluginLoader _plugins;
    private readonly TextWriterLogWriter? _logWriter;
    private readonly string? _logFile;
    private readonly SemaphoreSlim? _runSlots;
    private WorkflowSession _current;
    private WorkflowSession? _retired;
    private int _leases;
    private bool _disposed;

    private WorkflowSessionHost(
        CliOptions options,
        PluginLoader plugins,
        TextWriterLogWriter? logWriter,
        string? logFile,
        WorkflowSession current)
    {
        _options = options;
        _plugins = plugins;
        _logWriter = logWriter;
        _logFile = logFile;
        _current = current;
        _runSlots = options.MaxConcurrentRuns > 0
            ? new SemaphoreSlim(options.MaxConcurrentRuns, options.MaxConcurrentRuns)
            : null;
    }

    /// <summary>Limits concurrent isolated workers, or <c>null</c> when the host asked for no limit.</summary>
    public SemaphoreSlim? RunSlots => _runSlots;

    /// <summary>Creates the host and loads the startup archive.</summary>
    public static WorkflowSessionHost Create(CliOptions options, bool forceExecution = false)
    {
        var plugins = new PluginLoader();
        var workspace = WorkspaceRule.Normalize(options.Workspace);
        var logFile = ResolveLogFile(options.LogFile, workspace);

        // Only an in-process run writes through this writer. In isolated mode the worker opens the same file
        // itself; two writers with FileShare.Read would collide.
        var logWriter = (forceExecution || options.InProcessExecution) && logFile is not null
            ? TextWriterLogWriter.For(logFile)
            : null;

        try
        {
            var request = new ArchiveRequest(options.Archive!, options.Manifest, options.Assemblies, options.RootType);
            var session = WorkflowSession.Create(
                options, request, plugins, logWriter, logFile, options.RedirectOnError,
                executionEnabled: forceExecution ? true : null,
                inProcessExecution: forceExecution ? true : null,
                ownsLogWriter: false);

            return new WorkflowSessionHost(options, plugins, logWriter, logFile, session);
        }
        catch
        {
            logWriter?.Dispose();
            throw;
        }
    }

    /// <summary>The session every new call should use. Prefer <see cref="Acquire"/> when the session is held.</summary>
    public WorkflowSession Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Pins the current session for the duration of one call.</summary>
    public SessionLease Acquire()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WorkflowSessionHost));
            _leases++;
            return new SessionLease(this, _current);
        }
    }

    internal void Release()
    {
        WorkflowSession? toDispose = null;
        lock (_gate)
        {
            _leases--;
            if (_leases == 0 && _retired is { } retired)
            {
                toDispose = retired;
                _retired = null;
            }
        }

        // Disposal closes the scope (and its toolkit). It is cheap; doing it on the releasing thread is simpler than
        // carrying a second async path through every tool call. A failure here must not turn a completed tool call
        // into an exception — the session is retired either way.
        try { toDispose?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { /* best-effort retirement */ }
    }

    /// <summary>Invokes a native tool on the current session.</summary>
    public async ValueTask<object?> InvokeToolAsync(
        string name, AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        using var lease = Acquire();
        return await lease.Session.InvokeToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the loaded archive. The old session is retired until its last call finishes.</summary>
    /// <param name="request">What to load.</param>
    /// <param name="redirectOnError">The new session's redirect target, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <exception cref="InvalidOperationException">In-process execution is on; restart to switch archives.</exception>
    public async Task<WorkflowSession> ReloadAsync(
        ArchiveRequest request, int? redirectOnError, CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool inProcess;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(WorkflowSessionHost));
                inProcess = _current.InProcessExecution;
            }

            if (inProcess)
                throw new InvalidOperationException(
                    "LoadArchive is unavailable with --in-process-execution: the run handles belong to the session being replaced. " +
                    "Restart the server to switch archives, or use the isolated execution mode.");

            // Build first. A failed load leaves the current session untouched.
            var next = WorkflowSession.Create(
                _options, request, _plugins, _logWriter, _logFile, redirectOnError,
                executionEnabled: _current.ExecutionEnabled,
                inProcessExecution: _current.InProcessExecution,
                ownsLogWriter: false);

            WorkflowSession? old;
            lock (_gate)
            {
                old = _current;
                _current = next;
                if (_leases > 0) _retired = old;
                else old = null;
            }

            if (old is not null)
                await old.DisposeAsync().ConfigureAwait(false);

            return next;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private static string? ResolveLogFile(string? path, string? workspace)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var full = Path.GetFullPath(path);
        WorkspaceRule.Ensure(workspace, full, "Log file");
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        return full;
    }

    public async ValueTask DisposeAsync()
    {
        WorkflowSession? current;
        WorkflowSession? retired;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            current = _current;
            retired = _retired;
            _retired = null;
        }

        await current.DisposeAsync().ConfigureAwait(false);
        if (retired is not null) await retired.DisposeAsync().ConfigureAwait(false);
        _logWriter?.Dispose();
        _runSlots?.Dispose();
        _reloadGate.Dispose();
    }
}

/// <summary>A session pinned for one call; disposing it lets a reload finish retiring that session.</summary>
internal sealed class SessionLease(WorkflowSessionHost host, WorkflowSession session) : IDisposable
{
    public WorkflowSession Session { get; } = session;

    public void Dispose() => host.Release();
}
