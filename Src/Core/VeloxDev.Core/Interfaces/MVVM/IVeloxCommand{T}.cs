using System.Threading.Tasks;
using System.Windows.Input;

namespace VeloxDev.MVVM;

/// <summary>
/// An <see cref="IVeloxCommand"/> whose parameter type is known at the type level.
/// </summary>
/// <typeparam name="T">The type the command body receives.</typeparam>
/// <remarks>
/// <para>
/// <b>This is type information, not a compile-time guarantee.</b> Because this interface derives from
/// <see cref="IVeloxCommand"/>, the <c>object?</c> overloads it inherits — <see cref="IVeloxCommand.ExecuteAsync"/>,
/// <see cref="ICommand.Execute"/> and <see cref="ICommand.CanExecute"/> — stay reachable from a reference typed
/// <see cref="IVeloxCommand{T}"/>. A call passing the wrong type therefore still compiles, and still ends as a
/// <see cref="CommandEventType.Failed"/> execution carrying an <see cref="System.InvalidCastException"/>. What the
/// typed members buy is a readable signature and a strongly typed <c>CanExecute{Name}Command</c> hook on a
/// generated command.
/// </para>
/// <para>
/// Deriving from <see cref="IVeloxCommand"/> rather than standing alone is deliberate — a typed command has to
/// stay bindable and has to keep the lock, queue and interrupt surface. A standalone interface would put the
/// <c>object?</c> overloads out of reach and so make a wrong argument a compile error, at the cost of both.
/// </para>
/// <para>
/// A value type <typeparamref name="T"/> still boxes: parameters travel the pipeline as
/// <see cref="object"/>.
/// </para>
/// </remarks>
/// <seealso cref="VeloxCommand{T}"/>
public interface IVeloxCommand<T> : IVeloxCommand
{
    /// <summary>
    /// Whether the command can run with <paramref name="parameter"/> right now.
    /// </summary>
    /// <param name="parameter">The argument the body would receive.</param>
    /// <returns><see langword="true"/> when an execution would not be refused.</returns>
    bool CanExecute(T parameter);

    /// <summary>
    /// Starts an execution, handing <paramref name="parameter"/> to the body.
    /// </summary>
    /// <param name="parameter">The argument for the body.</param>
    /// <remarks>
    /// Returns once the call has been accepted, not once the body has finished — the same contract as
    /// <see cref="ICommand.Execute"/>.
    /// </remarks>
    void Execute(T parameter);

    /// <summary>
    /// Starts an execution and completes once the call has been accepted.
    /// </summary>
    /// <param name="parameter">The argument for the body.</param>
    /// <returns>A task that completes when the call has been queued or started.</returns>
    /// <remarks>
    /// The awaitable way to observe one execution's outcome is
    /// <see cref="VeloxCommandExtensions.ExecuteAndWaitAsync(IVeloxCommand, object?, System.Threading.CancellationToken)"/>.
    /// </remarks>
    Task ExecuteAsync(T parameter);
}
