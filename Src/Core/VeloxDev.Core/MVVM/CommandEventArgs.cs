namespace VeloxDev.MVVM;

/// <summary>
/// Handles one lifecycle stage of an execution.
/// </summary>
/// <param name="e">The stage that was reached.</param>
public delegate void CommandEventHandler(CommandEventArgs e);

/// <summary>
/// One stage of one execution, as reported by <see cref="IVeloxCommand"/>'s events.
/// </summary>
/// <remarks>
/// <para>
/// Every stage of an execution carries the same <see cref="Parameter"/> and <see cref="Cts"/>, so a handler can
/// correlate the stages of a single execution with each other.
/// </para>
/// <para>
/// <see cref="Exception"/> is populated only on <see cref="CommandEventType.Failed"/>; every other stage,
/// including <see cref="CommandEventType.Exited"/>, leaves it <see langword="null"/>. Do not treat
/// <see cref="CommandEventType.Exited"/> as a success signal.
/// </para>
/// </remarks>
public class CommandEventArgs(
    object? parameter,
    CommandEventType type,
    Exception? ex = null,
    CancellationTokenSource? cts = null)
{
    private CancellationTokenSource? _cts = cts;

    /// <summary>
    /// Creates the object-shaped projection of one execution stage, leaving <see cref="Parameter"/> to a
    /// subclass.
    /// </summary>
    /// <param name="type">The stage to report.</param>
    /// <param name="ex">The failure the stage should carry, if any.</param>
    /// <param name="cts">This execution's cancellation source, if it has one.</param>
    /// <remarks>
    /// For a subclass that holds the argument in its own typed field, which is what keeps a value-type parameter
    /// from being boxed on the way in. The base slot stays <see langword="null"/> and the subclass overrides
    /// <see cref="Parameter"/>.
    /// </remarks>
    protected CommandEventArgs(CommandEventType type, Exception? ex, CancellationTokenSource? cts)
        : this(null, type, ex, cts)
    {
    }

    /// <summary>
    /// The argument passed to <see cref="System.Windows.Input.ICommand.Execute"/>.
    /// </summary>
    /// <remarks>
    /// Virtual so that <see cref="CommandEventArgs{TParam, TResult}"/> can hand back its own typed argument.
    /// Reading it through this member boxes a value type; reading the subclass's typed member does not.
    /// </remarks>
    public virtual object? Parameter { get; } = parameter;

    /// <summary>The failure that ended the execution, on <see cref="CommandEventType.Failed"/> only.</summary>
    public Exception? Exception { get; } = ex;

    /// <summary>The lifecycle stage this instance reports.</summary>
    public CommandEventType EventType { get; } = type;

    // 这次执行的取消源；命令体的形参里没有 token 的形态下为 null。
    // 故意不公开：命令在本次执行结束时就会把它释放掉，暴露出去只会让人从 Exited 里读到一个正在失效的对象。
    // Interrupt/Clear 经它取消，命令之外没有第二个取用者。
    internal CancellationTokenSource? Cts
    {
        get => _cts;
        set => _cts = value;
    }

    /// <summary>
    /// Takes ownership of <see cref="Cts"/>, leaving the property <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Exactly one caller can win, which is what makes disposal safe to attempt from more than one place at
    /// once. The winner is responsible for disposing it.
    /// </remarks>
    internal CancellationTokenSource? TakeCts() => Interlocked.Exchange(ref _cts, null);

    // 等结果的槽，只有 ExecuteAndWaitAsync 会挂上它。With() 产生的副本不带 —— 副本不该能收尾。
    internal TaskCompletionSource<CommandCompletion>? Completion { get; set; }

    private int _cancelReported;

    // 首次声明者胜，用来让一次执行只报一条 Canceled。理由见 VeloxCommand.RaiseCanceled。
    internal bool TryMarkCancelReported() => Interlocked.Exchange(ref _cancelReported, 1) == 0;

    // 恰好收尾一次。没有等的人在时是空操作。
    internal void Complete(CommandOutcome outcome, Exception? exception, object? result = null)
        => Completion?.TrySetResult(new CommandCompletion(outcome, exception, result));

    /// <summary>
    /// Projects this instance onto another lifecycle stage, keeping the parameter, the cancellation source and —
    /// unless another is given — the failure.
    /// </summary>
    /// <param name="newType">The stage to report.</param>
    /// <param name="ex">
    /// The failure the new instance should carry. When omitted, the receiver's own failure is kept instead.
    /// </param>
    /// <remarks>
    /// The command itself never hits that fallback: it projects from the execution's origin instance, which is
    /// created without an exception and never mutated, so <see cref="CommandEventType.Failed"/> is the only stage
    /// that comes out carrying one. The fallback only matters to a caller chaining <see cref="With"/> by hand.
    /// </remarks>
    public CommandEventArgs With(CommandEventType newType, Exception? ex = null)
        => new(Parameter, newType, ex ?? Exception, Cts);
}
