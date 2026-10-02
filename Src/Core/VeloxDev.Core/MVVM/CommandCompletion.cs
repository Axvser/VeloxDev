using System.Runtime.ExceptionServices;

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
public readonly struct CommandCompletion(
    CommandOutcome outcome,
    Exception? exception = null,
    object? result = null)
{
    /// <summary>How the execution ended.</summary>
    public CommandOutcome Outcome { get; } = outcome;

    /// <summary>The failure, on <see cref="CommandOutcome.Failed"/> only.</summary>
    public Exception? Exception { get; } = exception;

    /// <summary>
    /// What the body returned, boxed — <see langword="null"/> when it returned nothing or returned
    /// <see langword="null"/> itself.
    /// </summary>
    /// <remarks>
    /// A command whose body returns no value reports <see langword="null"/> here, so this alone cannot tell
    /// "returned null" from "has no return value". Only <see cref="CommandOutcome.Completed"/> carries a value;
    /// the other outcomes leave it <see langword="null"/>. A command built from a
    /// <c>Task&lt;T&gt;</c>-returning method is what fills it in.
    /// </remarks>
    public object? Result { get; } = result;

    /// <summary>Whether the body ran to completion without throwing.</summary>
    public bool Succeeded => Outcome == CommandOutcome.Completed;

    /// <summary>
    /// Returns <see cref="Result"/>, or throws what ended the execution.
    /// </summary>
    /// <returns>The body's value, boxed, or <see langword="null"/> when it returned none.</returns>
    /// <exception cref="Exception">
    /// The body threw. The original instance is rethrown with
    /// <see cref="ExceptionDispatchInfo"/> so the stack trace survives.
    /// </exception>
    /// <exception cref="OperationCanceledException">The execution was cancelled.</exception>
    /// <exception cref="InvalidOperationException">The call was refused because the command was locked.</exception>
    /// <remarks>
    /// This is what turns a completion into a value. Awaiting it is the only place a command throws to its
    /// caller — <see cref="IVeloxCommandCompletion.ExecuteAndWaitAsync"/> deliberately reports outcomes without
    /// throwing, so pick the one that matches how the caller wants to react.
    /// </remarks>
    public object? GetResultOrThrow()
    {
        switch (Outcome)
        {
            case CommandOutcome.Completed:
                return Result;

            case CommandOutcome.Failed:
                ExceptionDispatchInfo.Capture(Exception ?? new InvalidOperationException("The execution failed.")).Throw();
                return null;   // Throw() 永不返回；这一行只为让编译器看到所有路径都有返回值。

            case CommandOutcome.Canceled:
                throw new OperationCanceledException("The execution was cancelled before it produced a result.");

            default:
                throw new InvalidOperationException("The command was locked, so the call never ran.");
        }
    }

    /// <summary>
    /// Returns <see cref="Result"/> as <typeparamref name="TR"/>, or throws what ended the execution.
    /// </summary>
    /// <typeparam name="TR">The type the command body returns.</typeparam>
    /// <returns>The body's value.</returns>
    /// <exception cref="System.InvalidCastException">
    /// <see cref="Result"/> is not a <typeparamref name="TR"/> — the command was built for a different body.
    /// </exception>
    /// <inheritdoc cref="GetResultOrThrow()" path="/exception"/>
    public TR GetResultOrThrow<TR>() => (TR)GetResultOrThrow()!;

    /// <summary>The outcome, plus the failure message when there is one.</summary>
    public override string ToString() =>
        Exception is null ? Outcome.ToString() : $"{Outcome}: {Exception.Message}";
}
