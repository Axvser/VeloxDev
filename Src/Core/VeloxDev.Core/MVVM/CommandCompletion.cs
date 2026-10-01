namespace VeloxDev.MVVM;

/// <summary>
/// How a single execution ended.
/// </summary>
public enum CommandOutcome
{
    /// <summary>The body ran to completion without throwing.</summary>
    Completed,

    /// <summary>The body threw.</summary>
    Failed,

    /// <summary>
    /// The execution was cancelled — by a lifecycle event such as <see cref="IVeloxCommand.Interrupt"/> or
    /// <see cref="IVeloxCommand.Clear"/>, by the body honouring its token, or because it was still queued when
    /// <see cref="IVeloxCommand.Clear"/> emptied the queue. A dropped call is reported as cancelled rather than
    /// as a distinct outcome because, from the caller's side, it is the same answer: it will never run.
    /// </summary>
    Canceled,

    /// <summary>
    /// The call never ran because the command was locked. This one has no matching
    /// <see cref="CommandEventType"/> member: a refused call raises
    /// <see cref="CommandEventType.Canceled"/> and never reaches <see cref="CommandEventType.Exited"/>, so
    /// <see cref="CommandEventType"/> alone cannot tell a refusal apart from a cancellation.
    /// </summary>
    Refused,
}

/// <summary>
/// The result of one execution, as returned by <see cref="IVeloxCommandCompletion.ExecuteAndWaitAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the lifecycle events, this is per-call: it answers "how did <em>this</em> execution end", including
/// for the calls that never run at all. That is what makes it usable as an await.
/// </para>
/// <para>
/// A cancelled execution is a normal return, not a thrown exception — <see cref="Outcome"/> is
/// <see cref="CommandOutcome.Canceled"/>. Only the caller's own <see cref="System.Threading.CancellationToken"/>
/// aborts the wait, and that does throw.
/// </para>
/// </remarks>
public readonly struct CommandCompletion(CommandOutcome outcome, Exception? exception = null)
{
    /// <summary>How the execution ended.</summary>
    public CommandOutcome Outcome { get; } = outcome;

    /// <summary>The failure, on <see cref="CommandOutcome.Failed"/> only.</summary>
    public Exception? Exception { get; } = exception;

    /// <summary>Whether the body ran to completion without throwing.</summary>
    public bool Succeeded => Outcome == CommandOutcome.Completed;

    /// <summary>The outcome, plus the failure message when there is one.</summary>
    public override string ToString() =>
        Exception is null ? Outcome.ToString() : $"{Outcome}: {Exception.Message}";
}
