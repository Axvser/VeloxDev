namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Where a compiled run's log lines go.
/// <para>
/// Set on the session (<see cref="RuntimeContext.LogWriter"/>) to divert the run's diagnostics — the engine's own
/// lines and every node's <see cref="IRuntimeContext.Log"/>/<see cref="IRuntimeContext.Warn"/>/
/// <see cref="IRuntimeContext.Error"/> — somewhere other than the in-memory <see cref="IRuntimeContext.Logs"/>,
/// typically a file that is appended to for the life of the process.
/// </para>
/// <para>
/// <b>Lines arrive in the order they happened</b>, and a fan-out's branches interleave, so a writer that cares
/// about grouping must do that itself. <see cref="IRuntimeContext.Logs"/> receives the same lines in the same
/// order, which makes the two views comparable line for line.
/// </para>
/// <para>
/// <b>Writes happen on the thread driving the run</b> — normally the host's
/// <see cref="System.Threading.SynchronizationContext"/>, deliberately (see <c>RuntimeEngine.RunParallelAsync</c>).
/// A writer that touches the file system synchronously therefore puts that IO on that thread; queue the line and
/// drain it on a background thread if that matters.
/// </para>
/// <para>
/// A writer is not a filter and cannot change semantics: <see cref="IRuntimeContext.Error"/> and
/// <see cref="IRuntimeContext.Warn"/> still mark the run for a redirect, whatever the writer does with the text.
/// </para>
/// </summary>
/// <seealso cref="DelegateLogWriter"/>
/// <seealso cref="TextWriterLogWriter"/>
public interface ILogWriter
{
    /// <summary>Receives one already-formatted log line, prefix included.</summary>
    /// <param name="line">The line exactly as it appears in <see cref="IRuntimeContext.Logs"/>.</param>
    void Write(string line);
}

/// <summary>
/// A line the writer refused. Raised on the session so a broken sink is visible instead of merely quiet — the
/// line is still dropped, and the run still continues.
/// </summary>
/// <seealso cref="RuntimeContext.LogWriteFailed"/>
public sealed class LogWriteFailedEventArgs(string line, Exception error) : EventArgs
{
    /// <summary>The line that could not be written. It is in <see cref="IRuntimeContext.Logs"/> regardless.</summary>
    public string Line { get; } = line;

    /// <summary>What the writer threw.</summary>
    public Exception Error { get; } = error;
}

