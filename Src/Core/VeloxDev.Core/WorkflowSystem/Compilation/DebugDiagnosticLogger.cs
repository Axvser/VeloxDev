using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Non-blocking diagnostic logger for the workflow compiler.
/// Queues messages and flushes them on a background worker so the compiler
/// pipeline is never blocked by I/O. Supports an optional user-configurable
/// <see cref="TextWriter"/> for streaming output to any destination (file,
/// console, network, etc.). When no writer is provided, falls back to
/// <see cref="System.Diagnostics.Debug.WriteLine(string)"/>.
/// Only available in Debug builds via the <see cref="WorkflowCompiler(IDiagnosticLogger?)"/> constructor.
/// </summary>
/// <remarks>
/// The logger does <b>not</b> take ownership of the writer's lifetime — the
/// caller should dispose the <see cref="TextWriter"/> after the logger is
/// disposed.
/// </remarks>
public sealed class DebugDiagnosticLogger : IDiagnosticLogger, IDisposable
{
    private readonly ConcurrentQueue<DiagnosticContext> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly TextWriter? _writer;

    private const int FlushIntervalMs = 100;

    /// <summary>
    /// Starts a background flush loop. Messages are written to
    /// <see cref="System.Diagnostics.Debug.WriteLine(string)"/> by default.
    /// </summary>
    public DebugDiagnosticLogger()
        : this(null)
    {
    }

    /// <summary>
    /// Starts a background flush loop that writes to the specified
    /// <paramref name="writer"/>. Set <paramref name="writeLine"/> to
    /// <c>true</c> (default) to also forward messages to
    /// <see cref="System.Diagnostics.Debug.WriteLine(string)"/>.
    /// </summary>
    /// <param name="writer">
    /// Optional <see cref="TextWriter"/> for streaming output. The caller
    /// retains ownership and must dispose it independently.
    /// </param>
    /// <param name="writeLine">
    /// When <c>true</c>, messages are also written via
    /// <see cref="System.Diagnostics.Debug.WriteLine(string)"/>.
    /// Ignored when <paramref name="writer"/> is <c>null</c>.
    /// </param>
    public DebugDiagnosticLogger(TextWriter? writer, bool writeLine = true)
    {
        _writer = writer;
        var alsoDebug = writeLine && writer is not null;

        _worker = Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    DrainQueue(alsoDebug);
                    await Task.Delay(FlushIntervalMs, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on dispose — drain remaining items.
            }

            // Final drain on shutdown.
            DrainQueue(alsoDebug);
        });
    }

    /// <inheritdoc />
    public void Log(DiagnosticContext context) =>
        _queue.Enqueue(context);

    /// <inheritdoc />
    public void LogWarning(DiagnosticContext context) =>
        _queue.Enqueue(context);

    /// <inheritdoc />
    public void LogError(DiagnosticContext context) =>
        _queue.Enqueue(context);

    /// <summary>
    /// Stops the background worker and drains any remaining messages.
    /// Does <b>not</b> dispose the user-supplied <see cref="TextWriter"/>.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        try { _worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    private void DrainQueue(bool alsoDebug)
    {
        while (_queue.TryDequeue(out var context))
        {
            var formatted = context.ToString();

            if (_writer is not null)
            {
                _writer.WriteLine(formatted);
                _writer.Flush();
            }

            if (alsoDebug || _writer is null)
                Debug.WriteLine(formatted);
        }
    }

    }
