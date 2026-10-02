using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM;

/// <summary>
/// A <see cref="VeloxCommand"/> whose parameter type and return type are both known at the type level.
/// </summary>
/// <typeparam name="TP">The type the command body receives.</typeparam>
/// <typeparam name="TR">The type the command body returns.</typeparam>
/// <remarks>
/// <para>
/// Everything the untyped command does — the concurrency cap, the queue, <c>Lock</c>/<c>Interrupt</c>/<c>Clear</c>,
/// the eight lifecycle events — a typed one does too; it only narrows the entry point and hands the body's value
/// back. The note on <see cref="IVeloxCommand{TP,TR}"/> about this being type information rather than a
/// compile-time guarantee applies here unchanged.
/// </para>
/// <para>
/// Both parameter and value are boxed on their way through the pipeline, which carries them as
/// <see cref="object"/>.
/// </para>
/// </remarks>
/// <seealso cref="IVeloxCommand{TP,TR}"/>
public sealed class VeloxCommand<TP, TR> : VeloxCommand, IVeloxCommand<TP, TR>
{
    /// <summary>
    /// Creates a typed command from a body that receives <typeparamref name="TP"/> and a cancellation token, and
    /// returns <typeparamref name="TR"/>.
    /// </summary>
    /// <param name="command">The body to run. It receives a token, so an interrupted execution can observe it.</param>
    /// <param name="canExecute">The predicate behind <see cref="CanExecute(TP)"/>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public VeloxCommand(
        Func<TP, CancellationToken, Task<TR>> command,
        Predicate<TP>? canExecute = null,
        int semaphore = 1)
        : base(
            Adapted(command),
            AdaptedResult(command),
            canExecute is null ? null : Adapted(canExecute),
            semaphore)
    {
    }

    private VeloxCommand(
        Func<TP, CancellationToken, Task<TR>> command,
        Predicate<TP>? canExecute,
        int semaphore,
        bool isCtsNeeded)
        : base(
            Adapted(command),
            AdaptedResult(command),
            canExecute is null ? null : Adapted(canExecute),
            semaphore,
            isCtsNeeded)
    {
    }

    /// <summary>
    /// Creates a typed command from a body that takes the parameter, returns a value, and cannot be cancelled.
    /// </summary>
    /// <param name="command">The body to run; it never receives a token.</param>
    /// <param name="canExecute">The predicate behind <see cref="CanExecute(TP)"/>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose executions report their value.</returns>
    /// <remarks>
    /// Keeps the same <c>null</c>-token contract as
    /// <see cref="VeloxCommand.CreateTypedTaskOnlyWithParameter{T}(System.Func{T, System.Threading.Tasks.Task}, System.Predicate{T}, int)"/>,
    /// so a value-returning body does not silently acquire a cancellation source it can never observe.
    /// </remarks>
    public static VeloxCommand<TP, TR> CreateTaskOnlyWithResult(
        Func<TP, Task<TR>> command,
        Predicate<TP>? canExecute = null,
        int semaphore = 1)
        => new((value, _) => command(value), canExecute, semaphore, isCtsNeeded: false);

    /// <inheritdoc cref="IVeloxCommand{TP}.CanExecute(TP)"/>
    public bool CanExecute(TP parameter) => base.CanExecute(parameter);

    /// <inheritdoc cref="IVeloxCommand{TP}.Execute(TP)"/>
    public void Execute(TP parameter) => base.Execute(parameter);

    /// <inheritdoc cref="IVeloxCommand{TP}.ExecuteAsync(TP)"/>
    public Task ExecuteAsync(TP parameter) => base.ExecuteAsync(parameter);

    /// <inheritdoc cref="IVeloxCommand{TP,TR}.ExecuteAsync(TP, CancellationToken)"/>
    public async Task<TR> ExecuteAsync(TP parameter, CancellationToken cancellationToken)
        => (TR)(await base.ExecuteAsync((object?)parameter, cancellationToken).ConfigureAwait(false))!;

    /// <inheritdoc cref="IVeloxCommand{TP,TR}.Execute(TP, out TR)"/>
    public void Execute(TP parameter, out TR result)
    {
        base.Execute((object?)parameter, out object? boxed);
        result = (TR)boxed!;
    }

    // 承接类型擦除的那两跳。null 检查在构造时发生 —— 内联 lambda 的话基类收到的永远是个非 null 的委托，
    // 空命令体要等到第一次执行才炸。
    private static Func<object?, CancellationToken, Task> Adapted(Func<TP, CancellationToken, Task<TR>>? command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return (value, ct) => command((TP)value!, ct);
    }

    private static Func<object?, CancellationToken, Task<object?>> AdaptedResult(Func<TP, CancellationToken, Task<TR>> command)
        => async (value, ct) => (object?)(await command((TP)value!, ct).ConfigureAwait(false));

    // 校验始终可能收到 null 实参（ICommand.CanExecute(object?) 是公开的，框架会这样调）。
    // 值类型的 TP 在那种情况下 (TP)value 会抛，异常落在调用方线程上会崩掉 UI —— 一律答 false。
    private static Predicate<object?> Adapted(Predicate<TP> canExecute)
        => value => value is null && default(TP) is not null ? false : canExecute((TP)value!);
}
