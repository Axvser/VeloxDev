using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM;

/// <summary>
/// A command that can hand the value its body returned back to the caller.
/// </summary>
/// <remarks>
/// <para>
/// A command body that returns <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c> produces a value the pipeline
/// would otherwise discard — <see cref="IVeloxCommand.ExecuteAsync(object?)"/> returns as soon as the call is
/// accepted, and <see cref="IVeloxCommandCompletion.ExecuteAndWaitAsync(object?, CancellationToken)"/> reports
/// only how the execution ended. This is where that value comes out.
/// </para>
/// <para>
/// A separate interface rather than a member of <see cref="IVeloxCommand"/>: adding to the base would break every
/// hand-written implementer, and <c>net461</c>/<c>netstandard2.0</c> have no default interface members to soften
/// it. <see cref="VeloxCommand"/> implements this, so every generated command has it.
/// </para>
/// <para>
/// The value belongs to one execution, never to the command: two overlapping calls get their own.
/// </para>
/// </remarks>
/// <seealso cref="IVeloxCommand{TParam,TResult}"/>
public interface IVeloxCommandResult
{
    /// <summary>
    /// Runs the command and completes when <em>this</em> execution has ended, with the value the body returned.
    /// </summary>
    /// <param name="parameter">The argument to pass to the body.</param>
    /// <param name="cancellationToken">
    /// Abandons the wait only — it does not cancel the execution. Use <see cref="IVeloxCommand.Interrupt"/> or
    /// <see cref="IVeloxCommand.Clear"/> to stop work already under way.
    /// </param>
    /// <returns>The body's value, boxed, or <see langword="null"/> when it returned none.</returns>
    /// <exception cref="Exception">
    /// The body threw; the original instance is rethrown. A cancelled execution throws
    /// <see cref="OperationCanceledException"/>, and a call refused by
    /// <see cref="IVeloxCommand.Lock"/> throws <see cref="InvalidOperationException"/>.
    /// </exception>
    /// <remarks>
    /// Named and shaped to sit beside <see cref="IVeloxCommand.ExecuteAsync(object?)"/> rather than replace it:
    /// the one-argument call still returns as soon as the call is accepted, and only this overload waits.
    /// It carries no default for <paramref name="cancellationToken"/> deliberately — a default would make the
    /// one-argument call ambiguous between the two.
    /// </remarks>
    Task<object?> ExecuteAsync(object? parameter, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the command and blocks the calling thread until <em>this</em> execution has ended, then stores the
    /// body's value in <paramref name="result"/>.
    /// </summary>
    /// <param name="parameter">The argument to pass to the body.</param>
    /// <param name="result">The body's value, boxed, or <see langword="null"/> when it returned none.</param>
    /// <exception cref="Exception">
    /// Same outcomes as <see cref="ExecuteAsync(object?, CancellationToken)"/>. On any of them this throws and
    /// <paramref name="result"/> is assigned <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This blocks on the queue as well as on the body: if the command is at its concurrency cap or locked, the
    /// calling thread waits for a slot. The pipeline runs with <c>ConfigureAwait(false)</c> throughout, so it
    /// will not deadlock on a synchronisation context — but it does occupy a thread for the whole execution.
    /// </para>
    /// <para>
    /// Calling this from inside the body of the same command deadlocks on the command's own lock. Reach for
    /// <see cref="ExecuteAsync(object?, CancellationToken)"/> unless the caller is genuinely synchronous.
    /// </para>
    /// </remarks>
    void Execute(object? parameter, out object? result);
}
