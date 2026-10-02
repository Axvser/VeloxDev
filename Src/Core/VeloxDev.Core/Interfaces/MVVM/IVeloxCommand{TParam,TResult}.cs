using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM;

/// <summary>
/// A command whose parameter type and return type are both known at the type level.
/// </summary>
/// <typeparam name="TParam">The type the command body receives.</typeparam>
/// <typeparam name="TResult">The type the command body returns.</typeparam>
/// <remarks>
/// <para>
/// <b>This is type information, not a compile-time guarantee.</b> Because this interface derives from
/// <see cref="IVeloxCommand"/>, the <c>object?</c> overloads it inherits stay reachable from a reference typed
/// <see cref="IVeloxCommand{TParam, TResult}"/>. A call passing the wrong type therefore still compiles, and
/// still ends as a <see cref="CommandOutcome.Failed"/> execution carrying an
/// <see cref="System.InvalidCastException"/>. What the typed members buy is a readable signature, a strongly
/// typed validator hook, and an unboxed path through the pipeline.
/// </para>
/// <para>
/// Deriving from <see cref="IVeloxCommand"/> rather than standing alone is deliberate — a typed command has to
/// stay bindable and has to keep the lock, queue and interrupt surface.
/// </para>
/// <para>
/// A command with no return value uses <see cref="object"/> for <typeparamref name="TResult"/> and always yields
/// <see langword="null"/>. That is what makes this the minimum shape: every typed command has a parameter and a
/// result, and only the parameter count varies.
/// </para>
/// </remarks>
/// <seealso cref="VeloxCommand{TParam, TResult}"/>
/// <seealso cref="IVeloxCommandEvents{TParam, TResult}"/>
public interface IVeloxCommand<TParam, TResult> : IVeloxCommand
{
    /// <summary>
    /// Whether an execution with <paramref name="parameter"/> would be accepted right now.
    /// </summary>
    /// <param name="parameter">The argument the body would receive.</param>
    /// <returns><see langword="true"/> when an execution would not be refused.</returns>
    /// <remarks>
    /// Reflects the predicate and the lock only — never the queue. At the concurrency cap this still answers
    /// <see langword="true"/>, because the call would be parked rather than dropped.
    /// </remarks>
    bool CanExecute(TParam parameter);

    /// <summary>
    /// Starts an execution, handing <paramref name="parameter"/> to the body.
    /// </summary>
    /// <param name="parameter">The argument for the body.</param>
    /// <remarks>
    /// Returns once the call has been accepted, not once the body has finished — the same contract as
    /// <see cref="System.Windows.Input.ICommand.Execute"/>.
    /// </remarks>
    void Execute(TParam parameter);

    /// <summary>
    /// Starts an execution and completes once the call has been accepted.
    /// </summary>
    /// <param name="parameter">The argument for the body.</param>
    /// <returns>A task that completes when the call has been queued or started.</returns>
    Task ExecuteAsync(TParam parameter);

    /// <summary>
    /// Runs the command and completes when <em>this</em> execution has ended, with the value the body returned.
    /// </summary>
    /// <param name="parameter">The argument for the body.</param>
    /// <param name="cancellationToken">
    /// Abandons the wait only — it does not cancel the execution. Use <see cref="IVeloxCommand.Interrupt"/> or
    /// <see cref="IVeloxCommand.Clear"/> to stop work already under way.
    /// </param>
    /// <returns>The body's value.</returns>
    /// <exception cref="System.Exception">
    /// The body threw; the original instance is rethrown. A cancelled execution throws
    /// <see cref="OperationCanceledException"/>, and a call refused by <see cref="IVeloxCommand.Lock"/> throws
    /// <see cref="System.InvalidOperationException"/>.
    /// </exception>
    /// <remarks>
    /// Two arguments rather than one so that it overloads <see cref="IVeloxCommand.ExecuteAsync(object?)"/>
    /// instead of hiding it: a one-argument call still returns as soon as the call is accepted.
    /// </remarks>
    Task<TResult> ExecuteAsync(TParam parameter, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the command and blocks the calling thread until <em>this</em> execution has ended, then stores the
    /// body's value in <paramref name="result"/>.
    /// </summary>
    /// <param name="parameter">The argument for the body.</param>
    /// <param name="result">The body's value.</param>
    /// <exception cref="System.Exception">Same outcomes as the awaitable twin.</exception>
    /// <remarks>
    /// Blocks on the queue as well as on the body, and deadlocks if called from inside the body of the same
    /// command. Prefer the awaitable twin unless the caller is genuinely synchronous.
    /// </remarks>
    void Execute(TParam parameter, out TResult result);
}
