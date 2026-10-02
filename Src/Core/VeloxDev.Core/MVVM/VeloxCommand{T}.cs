using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM;

/// <summary>
/// A <see cref="VeloxCommand"/> whose parameter type is known at the type level.
/// </summary>
/// <typeparam name="T">The type the command body receives.</typeparam>
/// <remarks>
/// <para>
/// Everything the untyped command does — the concurrency cap, the queue, <c>Lock</c>/<c>Interrupt</c>/<c>Clear</c>,
/// the eight lifecycle events — a typed one does too; it only narrows the entry point. The three added members
/// forward to the inherited <c>object?</c> ones, so the note on <see cref="IVeloxCommand{T}"/> about this being
/// type information rather than a compile-time guarantee applies here unchanged.
/// </para>
/// <para>
/// A value type <typeparamref name="T"/> boxes on the way in, because the pipeline carries its parameter as
/// <see cref="object"/>.
/// </para>
/// </remarks>
/// <seealso cref="IVeloxCommand{T}"/>
/// <seealso cref="VeloxCommand.CreateTypedTaskOnlyWithParameter{T}(System.Func{T, System.Threading.Tasks.Task}, System.Predicate{T}, int)"/>
public sealed class VeloxCommand<T> : VeloxCommand, IVeloxCommand<T, object?>
{
    /// <summary>
    /// Creates a typed command from a body that receives <typeparamref name="T"/> and a cancellation token.
    /// </summary>
    /// <param name="command">The body to run. It receives a token, so an interrupted execution can observe it.</param>
    /// <param name="canExecute">The predicate behind <see cref="CanExecute(T)"/>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public VeloxCommand(
        Func<T, CancellationToken, Task> command,
        Predicate<T>? canExecute = null,
        int semaphore = 1)
        : base(
            Adapted(command),
            canExecute is null ? null : Adapted(canExecute),
            semaphore)
    {
    }

    /// <inheritdoc cref="IVeloxCommand{T}.CanExecute(T)"/>
    public bool CanExecute(T parameter) => base.CanExecute(parameter);

    /// <inheritdoc cref="IVeloxCommand{T}.Execute(T)"/>
    public void Execute(T parameter) => base.Execute(parameter);

    /// <inheritdoc cref="IVeloxCommand{T}.ExecuteAsync(T)"/>
    public Task ExecuteAsync(T parameter) => base.ExecuteAsync(parameter);

    // 无返回值的强类型命令用 object? 当结果类型 —— 结果通道始终在，只是恒为 null。
    /// <inheritdoc cref="IVeloxCommand{TP,TR}.ExecuteAsync(TP, CancellationToken)"/>
    public Task<object?> ExecuteAsync(T parameter, CancellationToken cancellationToken)
        => base.ExecuteAsync((object?)parameter, cancellationToken);

    /// <inheritdoc cref="IVeloxCommand{TP,TR}.Execute(TP, out TR)"/>
    public void Execute(T parameter, out object? result)
        => base.Execute((object?)parameter, out result);

    // 承接类型擦除的那一跳。用静态方法而不是内联 lambda 是为了让 null 检查在构造时就发生 ——
    // 内联的话基类收到的永远是个非 null 的 lambda，空命令体要等到第一次执行才炸。
    private static Func<object?, CancellationToken, Task> Adapted(Func<T, CancellationToken, Task>? command)
        => command is null
            ? throw new ArgumentNullException(nameof(command))
            : (value, ct) => command((T)value!, ct);

    // 校验始终可能收到 null 实参 —— ICommand.CanExecute(object?) 是公开的，WPF 在模板应用阶段
    // 就会带着 null 调它一次。此时值类型的 (T)value 会抛，而异常落在 UI 线程上会直接崩掉整个应用，
    // 所以那种情况一律答 false。引用类型的 T 则原样把 null 交给校验器，由它自己判断。
    private static Predicate<object?> Adapted(Predicate<T> canExecute)
        => value => value is null && default(T) is not null ? false : canExecute((T)value!);
}
