namespace VeloxDev.MVVM;

using System.Runtime.ExceptionServices;

/// <summary>
/// An <see cref="System.Windows.Input.ICommand"/> whose executions are bounded, queued, cancellable and
/// observable.
/// </summary>
/// <remarks>
/// <para>
/// Beyond what <see cref="System.Windows.Input.ICommand"/> asks for, an execution here is subject to a
/// concurrency cap, can be parked in a queue rather than dropped, can be cancelled by
/// <see cref="Interrupt"/> or <see cref="Clear"/>, and reports the eight
/// <see cref="CommandEventType"/> stages.
/// </para>
/// <para>
/// <see cref="Execute(object?)"/> and <see cref="ExecuteAsync(object?)"/> return once the execution has been
/// accepted — queued or started — not once the body has finished. To act on the result, subscribe to
/// <see cref="Completed"/>, <see cref="Failed"/> or <see cref="Exited"/>, or use
/// <see cref="IVeloxCommandResult.ExecuteAsync(object?, System.Threading.CancellationToken)"/> when the body
/// returns a value.
/// </para>
/// <para>
/// Event subscribers run on whatever thread the pipeline happens to be on, so a handler that touches UI must
/// dispatch back itself — unless <see cref="EventContext"/> is set, which makes the command post them there
/// for you. A subscriber that throws does not disturb the command; subscribe to <see cref="HandlerException"/>
/// to observe such failures.
/// </para>
/// </remarks>
public abstract class CommandPipeline<TParam, TResult> :
    IVeloxCommand, IVeloxCommandCompletion, IVeloxCommandStatus, IVeloxCommandResult,
    IVeloxCommandEvents<TParam, TResult>, IDisposable
{
    /// <summary>
    /// Creates the pipeline every command shape shares.
    /// </summary>
    /// <param name="command">The body, with its value discarded. Used when <paramref name="resultCommand"/> is null.</param>
    /// <param name="resultCommand">The body with its value preserved, or <see langword="null"/> for a shape that has no result channel.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <param name="isCtsNeeded">
    /// Whether each execution gets a <see cref="CancellationTokenSource"/>. Pass <see langword="false"/> when the
    /// body takes no token — it cannot observe cancellation anyway, and the source would be pure allocation.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="semaphore"/> is less than 1.</exception>
    protected CommandPipeline(
        Func<TParam, CancellationToken, Task> command,
        Func<TParam, CancellationToken, Task<TResult>>? resultCommand,
        Predicate<TParam>? canExecute,
        int semaphore,
        bool isCtsNeeded)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _resultCommand = resultCommand;
        _canExecute = canExecute;
        _isCtsNeeded = isCtsNeeded;
        _maxConcurrency = semaphore >= 1
            ? semaphore
            : throw new ArgumentOutOfRangeException(nameof(semaphore), semaphore, "Semaphore must be >= 1.");
    }

    // 命令体返回值的通道。为 null 时命令体的值不做保留。
    private readonly Func<TParam, CancellationToken, Task<TResult>>? _resultCommand;

    // 命令体，值被丢弃的那条。没有结果通道的形状走它 —— 把无返回值的体统一成 Task<TResult>
    // 会给每次执行加一层 async 包装分配，比省下的那点开销更贵。
    private readonly Func<TParam, CancellationToken, Task> _command;
    private readonly Predicate<TParam>? _canExecute;

    // _stateLock 只保护下面四组状态，且是 SemaphoreSlim(1,1) —— 不可重入。
    // 因此绝不能在持锁期间调用任何用户代码（事件处理器、命令体），否则同线程再取锁即自锁。
    // 所有 RaiseCommandEvent 都在 Release() 之后；RaiseCanExecuteChanged 同理。
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Queue<CommandEventArgs<TParam, TResult>> _pendingQueue = new();
    // HashSet 而非 List：OnExecutionCompletedAsync 按项摘除，是热路径；顺序无关紧要。
    private readonly HashSet<CommandEventArgs<TParam, TResult>> _active = [];

    private int _maxConcurrency;
    private bool _isForceLocked = false;

    private bool _isCtsNeeded;
    private static readonly CancellationToken _defct = new();

    /// <summary>
    /// Reports that a subscriber of one of the command's events, or of
    /// <see cref="System.Windows.Input.ICommand.CanExecuteChanged"/>, threw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A throwing subscriber never disturbs the command: the lifecycle carries on either way and the exception
    /// is not rethrown. That is deliberate — a broken <c>Exited</c> handler must not strand the queue — but it
    /// also means the failure would otherwise be invisible. Subscribe here to observe it.
    /// </para>
    /// <para>
    /// With nothing subscribed the command behaves exactly as if this event did not exist. A handler that
    /// itself throws is ignored, so subscribing can never break the command that reported to you.
    /// </para>
    /// </remarks>
    // 转发到非泛型持有者。它绝不能搬进泛型基类 —— 那会让每个闭合类型各持有一个静态事件，
    // 而这个钩子是给全进程订阅的，「订阅了却没收到」是最难查的一种失效。
    public static event Action<Exception>? HandlerException
    {
        add => CommandDiagnostics.HandlerException += value;
        remove => CommandDiagnostics.HandlerException -= value;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public event CommandEventHandler? Created;
    /// <inheritdoc />
    public event CommandEventHandler? Enqueued;
    /// <inheritdoc />
    public event CommandEventHandler? Dequeued;
    /// <inheritdoc />
    public event CommandEventHandler? Started;
    /// <inheritdoc />
    public event CommandEventHandler? Completed;
    /// <inheritdoc />
    public event CommandEventHandler? Failed;
    /// <inheritdoc />
    public event CommandEventHandler? Canceled;
    /// <inheritdoc />
    public event CommandEventHandler? Exited;

    // 强类型那一面。显式实现是必需的：一个类不能有两个同名公开事件，而类上的名字已经被
    // object? 形状占用 —— 那是 IVeloxCommand 与绑定要的。槽是**每个命令实例**一个，不是每次执行。
    private CommandEventHandler<TParam, TResult>? _createdTyped;
    private CommandEventHandler<TParam, TResult>? _enqueuedTyped;
    private CommandEventHandler<TParam, TResult>? _dequeuedTyped;
    private CommandEventHandler<TParam, TResult>? _startedTyped;
    private CommandEventHandler<TParam, TResult>? _completedTyped;
    private CommandEventHandler<TParam, TResult>? _failedTyped;
    private CommandEventHandler<TParam, TResult>? _canceledTyped;
    private CommandEventHandler<TParam, TResult>? _exitedTyped;

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Created
    { add => _createdTyped += value; remove => _createdTyped -= value; }

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Enqueued
    { add => _enqueuedTyped += value; remove => _enqueuedTyped -= value; }

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Dequeued
    { add => _dequeuedTyped += value; remove => _dequeuedTyped -= value; }

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Started
    { add => _startedTyped += value; remove => _startedTyped -= value; }

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Completed
    { add => _completedTyped += value; remove => _completedTyped -= value; }

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Failed
    { add => _failedTyped += value; remove => _failedTyped -= value; }

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Canceled
    { add => _canceledTyped += value; remove => _canceledTyped -= value; }

    event CommandEventHandler<TParam, TResult>? IVeloxCommandEvents<TParam, TResult>.Exited
    { add => _exitedTyped += value; remove => _exitedTyped -= value; }

    private CommandEventHandler<TParam, TResult>? TypedHandlerFor(CommandEventType type) => type switch
    {
        CommandEventType.Created => _createdTyped,
        CommandEventType.Enqueued => _enqueuedTyped,
        CommandEventType.Dequeued => _dequeuedTyped,
        CommandEventType.Started => _startedTyped,
        CommandEventType.Completed => _completedTyped,
        CommandEventType.Failed => _failedTyped,
        CommandEventType.Canceled => _canceledTyped,
        _ => _exitedTyped,
    };

    private void InvokeTyped(CommandEventHandler<TParam, TResult> handler, CommandEventArgs<TParam, TResult> args)
    {
        try
        {
            handler(args);
        }
        catch (Exception ex)
        {
            ReportHandlerException(ex);
        }
    }

    private static void PostEventTyped(
        SynchronizationContext context,
        CommandPipeline<TParam, TResult> command,
        CommandEventHandler<TParam, TResult> handler,
        CommandEventArgs<TParam, TResult> args)
        => context.Post(
            static state =>
            {
                var (target, h, a) = ((CommandPipeline<TParam, TResult>, CommandEventHandler<TParam, TResult>, CommandEventArgs<TParam, TResult>))state!;
                target.InvokeTyped(h, a);
            },
            (command, handler, args));

    /// <summary>
    /// The context lifecycle events are raised on, or <see langword="null"/> (the default) to raise them on
    /// whatever thread the pipeline happens to be on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this to the UI framework's context so subscribers stop marshalling by hand. It is off by default
    /// because raising on the pipeline's thread is what the command has always done, and because a subscriber
    /// that already marshals would otherwise pay for it twice.
    /// </para>
    /// <para>
    /// Raising through a context is asynchronous: events are posted in order, but one can reach its handler
    /// after the call that raised it has already returned. Leave this <see langword="null"/> where a handler
    /// must observe the command at the exact moment the event is raised.
    /// </para>
    /// </remarks>
    public SynchronizationContext? EventContext { get; set; }

    /// <summary>
    /// Whether an execution is running or waiting for a free slot.
    /// </summary>
    /// <remarks>
    /// Read without taking the command's own lock, so a concurrent update can leave this one step stale — that
    /// is deliberate, since a property getter must not block.
    /// </remarks>
    public bool IsBusy => _active.Count > 0 || _pendingQueue.Count > 0;

    /// <summary>How many executions are running right now.</summary>
    /// <remarks>
    /// Read without taking the command's own lock, so a concurrent update can leave this one step stale — that
    /// is deliberate, since a property getter must not block.
    /// </remarks>
    public int ActiveCount => _active.Count;

    /// <summary>How many calls are waiting for a free slot.</summary>
    /// <remarks>
    /// Read without taking the command's own lock, so a concurrent update can leave this one step stale — that
    /// is deliberate, since a property getter must not block.
    /// </remarks>
    public int PendingCount => _pendingQueue.Count;

    private static void ReportHandlerException(Exception exception) => CommandDiagnostics.Report(exception);

    // 事件默认在管线所在线程上直接发（零额外分配）；只有显式设了 EventContext
    // 且当前不在该上下文上时才 Post 过去。Post 是异步的，所以编组路径只该用在真正的 UI 场景。
    private bool RaisesInline(SynchronizationContext? context)
        => context is null || ReferenceEquals(context, SynchronizationContext.Current);

    private static void PostEvent(
        SynchronizationContext context,
        CommandPipeline<TParam, TResult> command,
        CommandEventHandler handler,
        CommandEventArgs args)
        => context.Post(
            static state =>
            {
                // 元组里装的是**基类** args —— 兜底处理器收的就是它，强类型投递走另一条路（见 RaiseCommandEventAs）。
                var (target, h, a) = ((CommandPipeline<TParam, TResult>, CommandEventHandler, CommandEventArgs))state!;
                target.Invoke(h, a);
            },
            (command, handler, args));

    private void Invoke(CommandEventHandler handler, CommandEventArgs args)
    {
        try
        {
            handler(args);
        }
        catch (Exception ex)
        {
            ReportHandlerException(ex);
        }
    }

    private void RaiseCanExecuteChanged()
    {
        var context = EventContext;
        if (RaisesInline(context))
        {
            InvokeCanExecuteChanged();
        }
        else
        {
            context!.Post(static state => ((CommandPipeline<TParam, TResult>)state!).InvokeCanExecuteChanged(), this);
        }
    }

    private void InvokeCanExecuteChanged()
    {
        try
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportHandlerException(ex);
        }
    }

    private void RaiseCommandEvent(
        CommandEventHandler? handler,
        CommandEventHandler<TParam, TResult>? typedHandler,
        CommandEventArgs<TParam, TResult> args)
    {
        // 一边都没订阅就什么都不做 —— 连上下文都不必读。
        if (handler is null && typedHandler is null)
        {
            return;
        }

        var context = EventContext;
        if (RaisesInline(context))
        {
            if (typedHandler is not null)
            {
                InvokeTyped(typedHandler, args);
            }

            if (handler is not null)
            {
                Invoke(handler, args);
            }
        }
        else
        {
            if (typedHandler is not null)
            {
                PostEventTyped(context!, this, typedHandler, args);
            }

            if (handler is not null)
            {
                PostEvent(context!, this, handler, args);
            }
        }
    }

    // 除 Created 外的每个 stage 都要一份换过 EventType 的副本，而副本是纯开销：
    // 没人订阅就不构造。（订阅全部 8 个与一个都不订，改动前的每执行分配完全相同 —— 副本是无条件的。）
    private void RaiseCommandEventAs(
        CommandEventHandler? handler,
        CommandEventArgs<TParam, TResult> item,
        CommandEventType type,
        Exception? ex = null)
    {
        var typedHandler = TypedHandlerFor(type);

        // 一边都没订阅就一个副本都不建。
        if (handler is null && typedHandler is null)
        {
            return;
        }

        // 一份副本，两副面孔共用：强类型处理器读强类型字段，object? 处理器读继承来的那个（读时才装箱）。
        var copy = item.With(type, ex);
        var context = EventContext;
        if (RaisesInline(context))
        {
            if (typedHandler is not null)
            {
                InvokeTyped(typedHandler, copy);
            }

            if (handler is not null)
            {
                Invoke(handler, copy);
            }
        }
        else
        {
            if (typedHandler is not null)
            {
                PostEventTyped(context!, this, typedHandler, copy);
            }

            if (handler is not null)
            {
                PostEvent(context!, this, handler, copy);
            }
        }
    }

    // 一次执行最多报一条 Canceled。Interrupt/Clear 会主动报（及时），命令体随后自己抛出的
    // OperationCanceledException 还会想再报一条（晚到）—— 后到的被 TryMarkCancelReported 挡掉，
    // 于是「先到的那条胜出」。不挡的话，按 CommandEventType 计数的 handler 会多数一次。
    private void RaiseCanceled(CommandEventArgs<TParam, TResult> item)
    {
        if (item.TryMarkCancelReported())
        {
            RaiseCommandEventAs(Canceled, item, CommandEventType.Canceled);
        }
    }

    /// <summary>Whether an execution with <paramref name="parameter"/> would be accepted right now.</summary>
    /// <param name="parameter">The argument a caller would pass. Boxed if it is a value type.</param>
    /// <returns><see langword="true"/> when an execution would not be refused.</returns>
    /// <remarks>
    /// The <c>object?</c>-shaped face of <see cref="CanExecuteCore"/>, reached through
    /// <see cref="System.Windows.Input.ICommand"/> and by binding. Neither a <see langword="null"/> argument for a
    /// value-typed parameter nor an argument of the wrong type is executable, and neither throws — the frameworks
    /// ask this during layout, where an exception would take the window down.
    /// </remarks>
    /// <inheritdoc />
    public bool CanExecute(object? parameter)
        => parameter is TParam typed
            ? CanExecuteCore(typed)
            : parameter is null && default(TParam) is null && CanExecuteCore(default!);

    /// <summary>Whether an execution with <paramref name="parameter"/> would be accepted right now.</summary>
    /// <param name="parameter">The argument a caller would pass, in its own type.</param>
    /// <returns><see langword="true"/> when an execution would not be refused.</returns>
    /// <remarks>
    /// Reflects the predicate and the lock only — never the queue. At the concurrency cap this still answers
    /// <see langword="true"/>, because the call would be parked rather than dropped.
    /// </remarks>
    protected bool CanExecuteCore(TParam parameter)
        => (_canExecute?.Invoke(parameter) ?? true) && !_isForceLocked;

    /// <inheritdoc />
    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    /// <inheritdoc />
    public void Notify() => RaiseCanExecuteChanged();

    /// <inheritdoc />
    public void Lock() => _ = LockAsync();
    /// <inheritdoc />
    public void Unlock() => _ = UnlockAsync();
    /// <inheritdoc />
    public void Interrupt() => _ = InterruptAsync();
    /// <inheritdoc />
    public void Clear() => _ = ClearAsync();
    /// <inheritdoc />
    public void Continue() => _ = ContinueAsync();

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> is less than 1.</exception>
    public void ChangeSemaphore(int s)
    {
        // 同步版是 `_ = ChangeSemaphoreAsync(...)`，异常若只在 async 方法里抛就没人接得住，
        // 于是变成未观察异常、静默丢失。所以这里必须先自己校验一次。
        if (s < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(s), s, "Semaphore must be >= 1.");
        }

        _ = ChangeSemaphoreAsync(s);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(object? parameter)
        => ExecuteCore(CommandEventArgs<TParam, TResult>.FromBoxed(parameter, CommandEventType.Created));

    /// <summary>Starts an execution and returns once the call has been accepted.</summary>
    /// <param name="parameter">The argument for the body, in its own type.</param>
    /// <returns>A task that completes when the call has been queued or started.</returns>
    protected Task ExecuteAsyncCore(TParam parameter)
        => ExecuteCore(new CommandEventArgs<TParam, TResult>(parameter, CommandEventType.Created));

    /// <inheritdoc cref="IVeloxCommandResult.ExecuteAsync(object?, CancellationToken)"/>
    public async Task<object?> ExecuteAsync(object? parameter, CancellationToken cancellationToken)
        => (await ExecuteAndWaitAsync(parameter, cancellationToken).ConfigureAwait(false)).GetResultOrThrow();

    /// <inheritdoc cref="IVeloxCommandResult.Execute(object?, out object?)"/>
    public void Execute(object? parameter, out object? result)
        => result = ExecuteAsync(parameter, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Runs the body and completes when <em>this</em> execution has ended, with the value it returned.
    /// </summary>
    /// <param name="parameter">The argument for the body, in its own type.</param>
    /// <param name="cancellationToken">
    /// Abandons the wait only — it does not cancel the execution. Use <see cref="Interrupt"/> or
    /// <see cref="Clear"/> to stop work already under way.
    /// </param>
    /// <returns>The body's value.</returns>
    /// <exception cref="Exception">
    /// The body threw; the original instance is rethrown. A cancelled execution throws
    /// <see cref="OperationCanceledException"/>, and a call refused by <see cref="Lock"/> throws
    /// <see cref="InvalidOperationException"/>.
    /// </exception>
    /// <remarks>
    /// The strongly typed counterpart of <see cref="ExecuteAndWaitAsync(object?, CancellationToken)"/>: the
    /// argument and the value both stay in their own types, and neither is boxed on the way through.
    /// </remarks>
    protected async Task<TResult> ExecuteAndWaitTypedAsync(TParam parameter, CancellationToken cancellationToken)
    {
        var sink = new TaskCompletionSource<TypedCompletion<TResult>>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 这个 token 只放弃等待，不取消执行 —— 与装箱那条同一条规矩。
        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(
                static state =>
                {
                    var (source, token) = ((TaskCompletionSource<TypedCompletion<TResult>>, CancellationToken))state!;
                    source.TrySetCanceled(token);
                },
                (sink, cancellationToken))
            : default;

        var item = new CommandEventArgs<TParam, TResult>(parameter, CommandEventType.Created) { TypedCompletion = sink };
        await ExecuteCore(item).ConfigureAwait(false);

        return Unwrap(await sink.Task.ConfigureAwait(false));
    }

    /// <summary>
    /// Runs the body and blocks the calling thread until this execution has ended, then returns the value.
    /// </summary>
    /// <param name="parameter">The argument for the body, in its own type.</param>
    /// <returns>The body's value.</returns>
    /// <exception cref="Exception">
    /// Same outcomes as <see cref="ExecuteAndWaitTypedAsync(TParam, CancellationToken)"/>.
    /// </exception>
    /// <remarks>
    /// Blocks on the queue as well as on the body, and deadlocks if called from inside the body of the same
    /// command. Prefer the awaitable twin unless the caller is genuinely synchronous.
    /// </remarks>
    protected TResult ExecuteAndWaitTyped(TParam parameter)
        => ExecuteAndWaitTypedAsync(parameter, CancellationToken.None).GetAwaiter().GetResult();

    private static TResult Unwrap(in TypedCompletion<TResult> completion)
    {
        switch (completion.Outcome)
        {
            case CommandOutcome.Completed:
                return completion.Result;

            case CommandOutcome.Failed:
                ExceptionDispatchInfo.Capture(completion.Exception ?? new InvalidOperationException("The execution failed.")).Throw();
                return default!;   // Throw() 永不返回；这一行只为让编译器看到所有路径都有返回值。

            case CommandOutcome.Canceled:
                throw new OperationCanceledException("The execution was cancelled before it produced a value.");

            default:
                throw new InvalidOperationException("The command was locked, so the call never ran.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Completes when <em>this</em> execution has ended, including the calls that never run: one refused by a
    /// lock reports <see cref="CommandOutcome.Refused"/>, and one dropped from the queue by
    /// <see cref="ClearAsync"/> reports <see cref="CommandOutcome.Canceled"/>. Neither raises
    /// <see cref="Exited"/>, so neither is observable through the events at all.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired first.</exception>
    public async Task<CommandCompletion> ExecuteAndWaitAsync(
        object? parameter, CancellationToken cancellationToken = default)
    {
        var sink = new TaskCompletionSource<CommandCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 这个 token 只放弃等待，不取消执行 —— 取消执行是 Interrupt / Clear 的职责，
        // 它们会连带清空整个命令，不该由一次调用的 token 触发。
        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(
                static state =>
                {
                    var (source, token) = ((TaskCompletionSource<CommandCompletion>, CancellationToken))state!;
                    source.TrySetCanceled(token);
                },
                (sink, cancellationToken))
            : default;

        var item = CommandEventArgs<TParam, TResult>.FromBoxed(parameter, CommandEventType.Created);
        item.Completion = sink;
        await ExecuteCore(item).ConfigureAwait(false);

        return await sink.Task.ConfigureAwait(false);
    }

    // ExecuteAsync 与 ExecuteAndWaitAsync 的唯一差别，就是有没有一个在等结果的 sink.
    private async Task ExecuteCore(CommandEventArgs<TParam, TResult> item)
    {

        if (_isCtsNeeded)
        {
            item.Cts = new();
        }

        RaiseCommandEvent(Created, _createdTyped, item);

        // 锁内只推进状态；事件与命令体的启动一律挪到 Release() 之后。
        // 尤其不能让 ExecuteCoreAsync 在锁内起跑 —— 它在第一个 await 之前就会同步发出 Started。
        var forceLocked = false;
        var enqueued = false;
        var startNow = false;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            forceLocked = _isForceLocked;
            if (!forceLocked)
            {
                if (_active.Count < _maxConcurrency)
                {
                    _active.Add(item);
                    startNow = true;
                }
                else
                {
                    _pendingQueue.Enqueue(item);
                    enqueued = true;
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (forceLocked)
        {
            item.Cts?.Cancel();
            RaiseCanceled(item);
            // 这条路径不经过 ExecuteCoreAsync，所以由这里负责释放与收尾。
            item.TakeCts()?.Dispose();
            if (item.Completion is not null)
            {
                item.Complete(CommandOutcome.Refused, null);
            }

            item.CompleteTyped(CommandOutcome.Refused, null, default!);
        }
        else if (enqueued)
        {
            RaiseCommandEventAs(Enqueued, item, CommandEventType.Enqueued);
        }
        else if (startNow)
        {
            _ = ExecuteCoreAsync(item);
        }

        Notify();
    }

    private async Task ExecuteCoreAsync(CommandEventArgs<TParam, TResult> item)
    {
        RaiseCommandEventAs(Started, item, CommandEventType.Started);

        // 结局在这里算出来，而不是从事件或 item 上读回来：等结果的 sink 不能依赖「有人订阅了 Failed」。
        var outcome = CommandOutcome.Completed;
        Exception? failure = null;

        // 命令体的返回值。这里只读一次 —— 管道调用命令体也只有这一处，所以它天然属于本次执行。
        // 类型是 TResult 而不是 object?：这一格一旦是 object?，赋值时就装箱了，泛型化就白做了。
        TResult result = default!;

        try
        {
            if (_resultCommand is not null)
            {
                result = _isCtsNeeded
                    ? await _resultCommand(item.ResolveParameter(), (item.Cts ?? new()).Token).ConfigureAwait(false)
                    : await _resultCommand(item.ResolveParameter(), _defct).ConfigureAwait(false);
            }
            else if (_isCtsNeeded)
            {
                await _command(item.ResolveParameter(), (item.Cts ?? new()).Token).ConfigureAwait(false);
            }
            else
            {
                await _command(item.ResolveParameter(), _defct).ConfigureAwait(false);
            }
            RaiseCommandEventAs(Completed, item, CommandEventType.Completed);
        }
        catch (OperationCanceledException)
        {
            outcome = CommandOutcome.Canceled;
            RaiseCanceled(item);
        }
        catch (Exception ex)
        {
            outcome = CommandOutcome.Failed;
            failure = ex;
            RaiseCommandEventAs(Failed, item, CommandEventType.Failed, ex);
        }
        finally
        {
            // 走到这里命令体一定已经结束 —— 无论成功、失败还是被取消，也无论它是否已被
            // Interrupt/Clear 从 _active 摘走。这是唯一一个「跑过的」执行都必经的收尾点，
            // 所以既在这里释放 token 源，也在这里给 sink 收尾。
            try
            {
                await OnExecutionCompletedAsync(item).ConfigureAwait(false);
            }
            finally
            {
                // 嵌一层 finally：OnExecutionCompletedAsync 抛异常时，收尾与释放一步都不能被跳过。
                                // 兜底 sink 只有经 object? 入口的调用方才挂得上；没挂就不必为它装箱。
                if (item.Completion is not null)
                {
                    item.Complete(outcome, failure, result);
                }

                item.CompleteTyped(outcome, failure, result);
                item.TakeCts()?.Dispose();
            }
        }
    }

    private async Task OnExecutionCompletedAsync(CommandEventArgs<TParam, TResult> completed)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _active.Remove(completed);
        }
        finally
        {
            _stateLock.Release();
        }

        RaiseCommandEventAs(Exited, completed, CommandEventType.Exited);

        // 不在这里 RaiseCanExecuteChanged：紧接着的 TryStartPendingAsync 结尾一定会发一次。
        await TryStartPendingAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LockAsync()
    {
        await LockCoreAsync().ConfigureAwait(false);
        Notify();
    }

    // 返回「此前是否已经锁着」。Interrupt/Clear 要用它把自己借走的锁还回去：
    // 调用方原本就锁着的话，它们必须继续保持锁定，否则排队项会被意外放行。
    private async Task<bool> LockCoreAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var alreadyLocked = _isForceLocked;
            _isForceLocked = true;
            return alreadyLocked;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnlockAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _isForceLocked = false;
        }
        finally
        {
            _stateLock.Release();
        }

        Notify();
        await TryStartPendingAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InterruptAsync()
    {
        List<CommandEventArgs<TParam, TResult>> activeToCancel = [];

        var wasLocked = await LockCoreAsync().ConfigureAwait(false);

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            activeToCancel.AddRange(_active);
            _active.Clear();
        }
        finally
        {
            _stateLock.Release();
        }

        foreach (var it in activeToCancel)
        {
            try
            {
                it.Cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 命令体可能已经自行跑完并释放了源 —— 那就不用取消了。
            }
            catch (AggregateException)
            {
                // 命令体在 token 上注册的取消回调抛了异常。这里必须吞掉：让它逃逸会跳过下面的
                // UnlockAsync，命令就永久锁死，此后每个排队项都再也跑不起来。
            }

            // 不给 sink 收尾：这一项的命令体还会自己走完 ExecuteCoreAsync 的 finally。
            RaiseCanceled(it);
        }

        if (!wasLocked)
        {
            await UnlockAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        List<CommandEventArgs<TParam, TResult>> activeToCancel = [];
        List<CommandEventArgs<TParam, TResult>> pendingToCancel = [];

        var wasLocked = await LockCoreAsync().ConfigureAwait(false);

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            activeToCancel.AddRange(_active);
            _active.Clear();

            while (_pendingQueue.Count > 0)
            {
                pendingToCancel.Add(_pendingQueue.Dequeue());
            }
        }
        finally
        {
            _stateLock.Release();
        }

        // 保持与排空一致的顺序：先给每个排队项发 Dequeued，再统一发 Canceled。
        foreach (var it in pendingToCancel)
        {
            RaiseCommandEventAs(Dequeued, it, CommandEventType.Dequeued);
        }

        foreach (var it in pendingToCancel)
        {
            // 排队项从未进入 ExecuteCoreAsync，所以源、收尾、信号三者都只能在这里做。
            // 它们的 token 没交给过任何命令体，所以 Cancel() 上没有用户回调，不会抛。
            it.Cts?.Cancel();
            RaiseCanceled(it);
            it.TakeCts()?.Dispose();
            it.CompleteTyped(CommandOutcome.Canceled, null, default!);

            if (it.Completion is not null)
            {
                it.Complete(CommandOutcome.Canceled, null);
            }
        }

        foreach (var it in activeToCancel)
        {
            try
            {
                it.Cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 同上：命令体已经跑完并释放过源。
            }
            catch (AggregateException)
            {
                // 与 InterruptAsync 同理：回调抛异常不能逃逸，否则 UnlockAsync 被跳过、命令永久锁死。
            }

            RaiseCanceled(it);
        }

        if (!wasLocked)
        {
            await UnlockAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ContinueAsync()
    {
        var locked = false;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            locked = _isForceLocked;
        }
        finally
        {
            _stateLock.Release();
        }

        if (locked)
        {
            return;
        }

        await TryStartPendingAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="semaphore"/> is less than 1.</exception>
    public async Task ChangeSemaphoreAsync(int semaphore)
    {
        if (semaphore < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(semaphore), semaphore, "Semaphore must be >= 1.");
        }

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _maxConcurrency = semaphore;
        }
        finally
        {
            _stateLock.Release();
        }

        await TryStartPendingAsync().ConfigureAwait(false);
    }

    private async Task TryStartPendingAsync()
    {
        List<CommandEventArgs<TParam, TResult>> toStart = [];

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            while (_pendingQueue.Count > 0 &&
                   _active.Count < _maxConcurrency &&
                   !_isForceLocked)
            {
                var next = _pendingQueue.Dequeue();
                _active.Add(next);
                toStart.Add(next);
            }
        }
        finally
        {
            _stateLock.Release();
        }

        foreach (var next in toStart)
        {
            RaiseCommandEventAs(Dequeued, next, CommandEventType.Dequeued);
            _ = ExecuteCoreAsync(next);
        }

        RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Releases the command's internal lock.
    /// </summary>
    /// <remarks>
    /// For teardown, and only once nothing is in flight — disposing while a call is queued or running makes that
    /// call's next lock acquisition throw. A command that is merely dropped needs no teardown: the lock holds no
    /// unmanaged resource. Disposal is final; a disposed command cannot be used again.
    /// </remarks>
    public void Dispose() => _stateLock.Dispose();
}
