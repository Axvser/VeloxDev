using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Cli;

/// <summary>One recorded tool call: what ran, how long it took, and whether it returned or threw.</summary>
internal sealed record ToolAuditEntry(
    string Tool,
    DateTimeOffset At,
    double DurationMs,
    bool Ok,
    int ResultChars,
    string Arguments,
    string? Error);

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
    private const int AuditCapacity = 500;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly CliOptions _options;
    private readonly PluginLoader _plugins;
    private readonly TextWriterLogWriter? _logWriter;
    private readonly string? _logFile;
    private readonly SemaphoreSlim? _runSlots;
    private readonly List<ToolAuditEntry> _audit = [];
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
        plugins.StrictVersions = options.StrictPluginVersions;
        plugins.CopyAssemblies = !options.NoAssemblyCopy;
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

    /// <summary>Invokes a native tool on the current session and records the call in the audit ring.</summary>
    public async ValueTask<object?> InvokeToolAsync(
        string name, AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var lease = Acquire();
        try
        {
            var result = await lease.Session.InvokeToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
            if (lease.Session.IsExecutionResultTool(name))
                result = AttachRunCsv(lease.Session, name, result);
            RecordToolCall(name, DescribeArguments(arguments), ok: true, ResultChars(result),
                Stopwatch.GetElapsedTime(started), error: null);
            return result;
        }
        catch (Exception ex)
        {
            RecordToolCall(name, DescribeArguments(arguments), ok: false, resultChars: 0,
                Stopwatch.GetElapsedTime(started), ex.Message);
            throw;
        }
    }

    /// <summary>Records a tool call that bypasses <see cref="InvokeToolAsync"/> (the archive-level MCP tools).</summary>
    public void RecordToolCall(
        string tool,
        string arguments,
        bool ok,
        int resultChars,
        TimeSpan duration,
        string? error = null)
    {
        lock (_gate)
        {
            _audit.Add(new ToolAuditEntry(
                tool,
                DateTimeOffset.UtcNow,
                Math.Round(duration.TotalMilliseconds, 3),
                ok,
                resultChars,
                arguments,
                error));
            if (_audit.Count > AuditCapacity)
                _audit.RemoveRange(0, _audit.Count - AuditCapacity);
        }
    }

    /// <summary>The most recent tool calls, newest last, as an MCP-shaped JSON envelope.</summary>
    public string GetAuditLog(int maxEntries = 0)
    {
        lock (_gate)
        {
            var total = _audit.Count;
            var take = maxEntries > 0 ? Math.Min(maxEntries, total) : total;
            var entries = _audit
                .Skip(total - take)
                .Select(entry => new
                {
                    tool = entry.Tool,
                    at = entry.At,
                    durationMs = entry.DurationMs,
                    ok = entry.Ok,
                    resultChars = entry.ResultChars,
                    arguments = entry.Arguments,
                    error = entry.Error,
                })
                .ToArray();

            return JsonSerializer.Serialize(new
            {
                status = "ok",
                entryCount = total,
                returnedCount = entries.Length,
                truncated = entries.Length < total,
                entries,
            });
        }
    }

    private static int ResultChars(object? result)
        => result switch
        {
            null => 0,
            string text => text.Length,
            _ => result.ToString()?.Length ?? 0,
        };

    private static object? AttachRunCsv(WorkflowSession session, string tool, object? result)
        => result switch
        {
            string text => session.AttachRunCsv(tool, text),
            JsonElement element when element.ValueKind == JsonValueKind.String
                => session.AttachRunCsv(tool, element.GetString() ?? string.Empty),
            _ => result,
        };

    // 参数只留摘要：审计要能回答「调了什么」，不需要把整份文档塞进环形缓冲。
    private static string DescribeArguments(AIFunctionArguments arguments)
    {
        try
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in arguments) map[pair.Key] = pair.Value;
            var json = JsonSerializer.Serialize(map);
            return json.Length <= 400 ? json : json[..400] + "…";
        }
        catch
        {
            return "{}";
        }
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

            if (_options.SingleArchive)
                throw new InvalidOperationException(
                    "LoadArchive is unavailable under --single-archive: this process serves one document for its lifetime, " +
                    "which is what keeps runtime IDs stable. Start a new server process for another archive.");

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
