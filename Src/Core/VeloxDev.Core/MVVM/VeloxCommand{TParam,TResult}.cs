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
/// The optimized shape: the argument and the value travel through the queue, the lock and the completion in
/// their own types, so neither is boxed. <see cref="VeloxCommand"/> is the same pipeline with both types erased
/// to <see cref="object"/>, and is what binding reaches.
/// </para>
/// <para>
/// This is the one-parameter member of the arity family; the rest are generated in
/// <c>CommandArities.cs</c>. A command with more than one parameter is a <c>VeloxCommand&lt;TParam1, …,
/// TResult&gt;</c> from that family, and a command with none has no typed form at all.
/// </para>
/// </remarks>
/// <seealso cref="IVeloxCommand{TParam, TResult}"/>
/// <seealso cref="CommandPipeline{TParam, TResult}"/>
public sealed class VeloxCommand<TParam, TResult> : CommandPipeline<TParam, TResult>, IVeloxCommand<TParam, TResult>
{
    /// <summary>
    /// Creates a typed command from a body that receives <typeparamref name="TParam"/> and a cancellation token,
    /// and returns <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="command">The body to run. It receives a token, so an interrupted execution can observe it.</param>
    /// <param name="canExecute">The predicate behind <see cref="CanExecute(TParam)"/>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public VeloxCommand(
        Func<TParam, CancellationToken, Task<TResult>> command,
        Predicate<TParam>? canExecute = null,
        int semaphore = 1)
        : base(command is null ? throw new ArgumentNullException(nameof(command)) : (parameter, ct) => command(parameter, ct),
               command,
               canExecute,
               semaphore,
               isCtsNeeded: true)
    {
    }

    /// <summary>
    /// Creates a typed command from a body that receives <typeparamref name="TParam"/> and cannot be cancelled.
    /// </summary>
    /// <param name="command">The body to run; it never receives a token.</param>
    /// <param name="canExecute">The predicate behind <see cref="CanExecute(TParam)"/>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Uses the pipeline's <c>null</c>-token channel rather than wrapping the body in a result-returning thunk:
    /// that wrapper would be an async state machine allocated on every execution, which costs more than the
    /// boxing this whole design removes. The declared result type is still the caller's, and the value is always
    /// <see langword="null"/>.
    /// </remarks>
    internal VeloxCommand(
        Func<TParam, CancellationToken, Task> command,
        Predicate<TParam>? canExecute,
        int semaphore,
        bool isCtsNeeded)
        : base(command, resultCommand: null, canExecute, semaphore, isCtsNeeded)
    {
    }

    /// <summary>
    /// Creates a typed command from a body that takes the parameter, returns a value, and cannot be cancelled.
    /// </summary>
    /// <param name="command">The body to run; it never receives a token.</param>
    /// <param name="canExecute">The predicate behind <see cref="CanExecute(TParam)"/>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose executions report their value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public static VeloxCommand<TParam, TResult> CreateTaskOnlyWithResult(
        Func<TParam, Task<TResult>> command,
        Predicate<TParam>? canExecute = null,
        int semaphore = 1)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new(
            (parameter, _) => command(parameter),
            canExecute,
            semaphore);
    }

    /// <inheritdoc cref="IVeloxCommand{TParam, TResult}.CanExecute(TParam)"/>
    public bool CanExecute(TParam parameter) => CanExecuteCore(parameter);

    /// <inheritdoc cref="IVeloxCommand{TParam, TResult}.Execute(TParam)"/>
    public void Execute(TParam parameter) => _ = ExecuteAsyncCore(parameter);

    /// <inheritdoc cref="IVeloxCommand{TParam, TResult}.ExecuteAsync(TParam)"/>
    public Task ExecuteAsync(TParam parameter) => ExecuteAsyncCore(parameter);

    /// <inheritdoc cref="IVeloxCommand{TParam, TResult}.ExecuteAsync(TParam, CancellationToken)"/>
    public Task<TResult> ExecuteAsync(TParam parameter, CancellationToken cancellationToken)
        => ExecuteAndWaitTypedAsync(parameter, cancellationToken);

    /// <inheritdoc cref="IVeloxCommand{TParam, TResult}.Execute(TParam, out TResult)"/>
    public void Execute(TParam parameter, out TResult result) => result = ExecuteAndWaitTyped(parameter);
}
