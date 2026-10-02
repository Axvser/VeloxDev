using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM;

/// <summary>
/// An <see cref="IVeloxCommand{TP}"/> that also knows the type its body returns.
/// </summary>
/// <typeparam name="TP">The type the command body receives.</typeparam>
/// <typeparam name="TR">The type the command body returns.</typeparam>
/// <remarks>
/// <para>
/// <b>This is type information, not a compile-time guarantee</b> — the same caveat as
/// <see cref="IVeloxCommand{TP}"/>. Because this derives from <see cref="IVeloxCommand{TP}"/>, which derives
/// from <see cref="IVeloxCommand"/>, the <c>object?</c> overloads stay reachable. A call passing the wrong type
/// still compiles and still ends as a <see cref="CommandOutcome.Failed"/> execution.
/// </para>
/// <para>
/// It does <em>not</em> derive from <see cref="IVeloxCommandResult"/>. Both declare a two-argument
/// <c>ExecuteAsync</c>, and a type implementing both would carry two members that differ only in their parameter
/// types — legal for every <typeparamref name="TP"/> except <see cref="object"/>, where they would collapse into
/// one signature. A command whose parameter is <c>object?</c> is never generated as a typed one, so that case
/// cannot arise from the generator.
/// </para>
/// <para>
/// A command with no return value uses <see cref="object"/> for <typeparamref name="TR"/> and always yields
/// <see langword="null"/>.
/// </para>
/// </remarks>
/// <seealso cref="VeloxCommand{TP,TR}"/>
public interface IVeloxCommand<TP, TR> : IVeloxCommand<TP>
{
    /// <summary>
    /// Runs the command and completes when <em>this</em> execution has ended, with the value the body returned.
    /// </summary>
    /// <param name="parameter">The argument to pass to the body.</param>
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
    /// Two arguments rather than one so that it overloads
    /// <see cref="IVeloxCommand.ExecuteAsync(object?)"/> instead of hiding it: a one-argument call still returns
    /// as soon as the call is accepted.
    /// </remarks>
    Task<TR> ExecuteAsync(TP parameter, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the command and blocks the calling thread until <em>this</em> execution has ended, then stores the
    /// body's value in <paramref name="result"/>.
    /// </summary>
    /// <param name="parameter">The argument to pass to the body.</param>
    /// <param name="result">The body's value.</param>
    /// <exception cref="System.Exception">Same outcomes as <see cref="ExecuteAsync(TP, CancellationToken)"/>.</exception>
    /// <remarks>
    /// Blocks on the queue as well as on the body, and deadlocks if called from inside the body of the same
    /// command. Prefer <see cref="ExecuteAsync(TP, CancellationToken)"/> unless the caller is genuinely
    /// synchronous.
    /// </remarks>
    void Execute(TP parameter, out TR result);
}
