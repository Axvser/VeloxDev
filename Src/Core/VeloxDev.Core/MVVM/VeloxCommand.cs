namespace VeloxDev.MVVM;

/// <summary>
/// The lifecycle stages of a single execution, reported through <see cref="IVeloxCommand"/>'s events.
/// </summary>
/// <remarks>
/// <para>
/// An execution that runs immediately reports <see cref="Created"/>, <see cref="Started"/>,
/// <see cref="Completed"/> and <see cref="Exited"/>. One that had to wait for a free slot reports
/// <see cref="Enqueued"/> and <see cref="Dequeued"/> around that same sequence. An execution that was refused
/// by a lock, or that was interrupted, reports <see cref="Canceled"/> in place of <see cref="Completed"/>; one
/// whose body threw reports <see cref="Failed"/>.
/// </para>
/// <para>
/// A stage can have more than one emitter, so the same value may arrive twice for one execution:
/// <see cref="Canceled"/> is raised by <see cref="IVeloxCommand.Interrupt"/> and <see cref="IVeloxCommand.Clear"/>
/// as well as by the body's own <see cref="OperationCanceledException"/>.
/// </para>
/// </remarks>
public enum CommandEventType : int
{
    None = 0,
    Created,   // Created
    Enqueued,  // Enqueued, waiting to run
    Dequeued,  // Dequeued, ready to execute
    Started,   // Execution actually started
    Completed, // Executed successfully
    Failed,    // Execution failed
    Canceled,  // Cancelled
    Exited     // Lifecycle ended
}

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
/// <see cref="Execute"/> and <see cref="ExecuteAsync"/> return once the execution has been accepted — queued
/// or started — not once the body has finished. To act on the result, subscribe to <see cref="Completed"/>,
/// <see cref="Failed"/> or <see cref="Exited"/>.
/// </para>
/// <para>
/// Event subscribers run on whatever thread the pipeline happens to be on, so a handler that touches UI must
/// dispatch back itself — unless <see cref="EventContext"/> is set, which makes the command post them there
/// for you. A subscriber that throws does not disturb the command; subscribe to <see cref="HandlerException"/>
/// to observe such failures.
/// </para>
/// </remarks>
public sealed class VeloxCommand(Func<object?, CancellationToken, Task> command,
                    Predicate<object?>? canExecute = null,
                    int semaphore = 1) : IVeloxCommand, IVeloxCommandCompletion, IVeloxCommandStatus
{
    /// <summary>
    /// Creates a command from a body that takes the parameter but cannot be cancelled.
    /// </summary>
    /// <remarks>
    /// The returned command still raises <see cref="IVeloxCommand.Canceled"/> when interrupted, but the body
    /// always runs to completion: it never receives a token to observe.
    /// </remarks>
    public static VeloxCommand CreateTaskOnlyWithParameter(
        Func<object?, Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (parameter, _) => { await command(parameter).ConfigureAwait(false); },
            canExecute,
            semaphore)
        {
            _isCtsNeeded = false
        };

    /// <summary>
    /// Creates a command from a body that takes only the cancellation token, and really can be cancelled.
    /// </summary>
    public static VeloxCommand CreateTaskOnlyWithCancellationToken(
        Func<CancellationToken, Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (_, ct) => { await command(ct).ConfigureAwait(false); },
            canExecute,
            semaphore);

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    /// <summary>
    /// Creates a command from a body that takes the parameter and returns a <see cref="ValueTask"/>.
    /// </summary>
    /// <remarks>
    /// Named rather than an overload of <see cref="CreateTaskOnlyWithParameter"/>. A
    /// <c>Func&lt;object?, ValueTask&gt;</c> sitting next to the <c>Task</c> one would make every
    /// <c>async</c> lambda call site ambiguous (CS0121) — and <c>async p =&gt; { … }</c> is how callers of this
    /// library write commands. It also stays <c>null</c>-token like its <c>Task</c> sibling: the body cannot be
    /// stopped, it can only be told that it was.
    /// </remarks>
    public static VeloxCommand CreateTaskOnlyWithValueTaskParameter(
        Func<object?, ValueTask> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (parameter, _) => { await command(parameter).ConfigureAwait(false); },
            canExecute,
            semaphore)
        {
            _isCtsNeeded = false
        };

    /// <summary>
    /// Creates a command from a body that takes only the cancellation token and returns a
    /// <see cref="ValueTask"/>, so it really can be cancelled.
    /// </summary>
    public static VeloxCommand CreateTaskOnlyWithValueTaskCancellationToken(
        Func<CancellationToken, ValueTask> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (_, ct) => { await command(ct).ConfigureAwait(false); },
            canExecute,
            semaphore);
#endif

    /// <summary>
    /// Creates a command from a body that takes nothing.
    /// </summary>
    /// <remarks>
    /// Like <see cref="CreateTaskOnlyWithParameter"/>, the body never receives a token, so an interrupted
    /// execution reports <see cref="CommandEventType.Canceled"/> without the body actually stopping.
    /// </remarks>
    public VeloxCommand(
        Func<Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : this(
            async (_, __) => await command().ConfigureAwait(false),
            canExecute,
            semaphore)
    {
        _isCtsNeeded = false;
    }

    /// <summary>
    /// Creates a command from a synchronous body that takes the parameter.
    /// </summary>
    /// <remarks>
    /// Like <see cref="CreateTaskOnlyWithParameter"/>, the body never receives a token, so an interrupted
    /// execution reports <see cref="CommandEventType.Canceled"/> without the body actually stopping.
    /// </remarks>
    public VeloxCommand(
        Action<object?> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : this(
            (parameter, _) => { command(parameter); return Task.CompletedTask; },
            canExecute,
            semaphore)
    {
        _isCtsNeeded = false;
    }

    /// <summary>
    /// Creates a command from a synchronous body that takes nothing.
    /// </summary>
    /// <remarks>
    /// Like <see cref="CreateTaskOnlyWithParameter"/>, the body never receives a token, so an interrupted
    /// execution reports <see cref="CommandEventType.Canceled"/> without the body actually stopping.
    /// </remarks>
    public VeloxCommand(
        Action command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : this(
            (_, __) => { command(); return Task.CompletedTask; },
            canExecute,
            semaphore)
    {
        _isCtsNeeded = false;
    }

    private readonly Func<object?, CancellationToken, Task> _command = command ?? throw new ArgumentNullException(nameof(command));
    private readonly Predicate<object?>? _canExecute = canExecute;

    // _stateLock 只保护下面四组状态，且是 SemaphoreSlim(1,1) —— 不可重入。
    // 因此绝不能在持锁期间调用任何用户代码（事件处理器、命令体），否则同线程再取锁即自锁。
    // 所有 RaiseCommandEvent 都在 Release() 之后；RaiseCanExecuteChanged 同理。
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Queue<CommandEventArgs> _pendingQueue = new();
    private readonly List<CommandEventArgs> _active = [];

    private int _maxConcurrency = semaphore >= 1
        ? semaphore
        : throw new ArgumentOutOfRangeException(nameof(semaphore), semaphore, "Semaphore must be >= 1.");
    private bool _isForceLocked = false;

    private bool _isCtsNeeded = true;
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
    public static event Action<Exception>? HandlerException;

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

    private static void ReportHandlerException(Exception exception)
    {
        try
        {
            HandlerException?.Invoke(exception);
        }
        catch
        {
            // 诊断钩子自己抛异常绝不能漏出去：Completed 是在 try 内发的，
            // 一旦逃逸就会被下面的 catch (Exception) 抓住，把一次成功的执行误报成 Failed。
        }
    }

    // 事件默认在管线所在线程上直接发（零额外分配）；只有显式设了 EventContext
    // 且当前不在该上下文上时才 Post 过去。Post 是异步的，所以编组路径只该用在真正的 UI 场景。
    private bool RaisesInline(SynchronizationContext? context)
        => context is null || ReferenceEquals(context, SynchronizationContext.Current);

    private static void PostEvent(
        SynchronizationContext context,
        VeloxCommand command,
        CommandEventHandler handler,
        CommandEventArgs args)
        => context.Post(
            static state =>
            {
                var (target, h, a) = ((VeloxCommand, CommandEventHandler, CommandEventArgs))state!;
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
            context!.Post(static state => ((VeloxCommand)state!).InvokeCanExecuteChanged(), this);
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

    private void RaiseCommandEvent(CommandEventHandler? handler, CommandEventArgs args)
    {
        if (handler is null)
        {
            return;
        }

        var context = EventContext;
        if (RaisesInline(context))
        {
            Invoke(handler, args);
        }
        else
        {
            PostEvent(context!, this, handler, args);
        }
    }

    // 除 Created 外的每个 stage 都要一份换过 EventType 的副本，而副本是纯开销：
    // 没人订阅就不构造。（订阅全部 8 个与一个都不订，改动前的每执行分配完全相同 —— 副本是无条件的。）
    private void RaiseCommandEventAs(
        CommandEventHandler? handler,
        CommandEventArgs item,
        CommandEventType type,
        Exception? ex = null)
    {
        if (handler is null)
        {
            return;
        }

        var context = EventContext;
        if (RaisesInline(context))
        {
            Invoke(handler, item.With(type, ex));
        }
        else
        {
            PostEvent(context!, this, handler, item.With(type, ex));
        }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
        => (_canExecute?.Invoke(parameter) ?? true) && !_isForceLocked;

    /// <inheritdoc />
    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    /// <inheritdoc />
    public void Notify() => RaiseCanExecuteChanged();

    /// <inheritdoc />
    public void Lock() => _ = LockAsync();
    /// <inheritdoc />
    public void UnLock() => _ = UnLockAsync();
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
    public Task ExecuteAsync(object? parameter) => ExecuteCore(parameter, sink: null);

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

        await ExecuteCore(parameter, sink).ConfigureAwait(false);
        return await sink.Task.ConfigureAwait(false);
    }

    // ExecuteAsync 与 ExecuteAndWaitAsync 的唯一差别，就是有没有一个在等结果的 sink。
    private async Task ExecuteCore(object? parameter, TaskCompletionSource<CommandCompletion>? sink)
    {
        var item = new CommandEventArgs(parameter, CommandEventType.Created) { Completion = sink };
        if (_isCtsNeeded)
        {
            item.Cts = new();
        }

        RaiseCommandEvent(Created, item);

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
            RaiseCommandEventAs(Canceled, item, CommandEventType.Canceled);
            // 这条路径不经过 ExecuteCoreAsync，所以由这里负责释放与收尾。
            item.TakeCts()?.Dispose();
            item.Complete(CommandOutcome.Refused, null);
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

    private async Task ExecuteCoreAsync(CommandEventArgs item)
    {
        RaiseCommandEventAs(Started, item, CommandEventType.Started);

        // 结局在这里算出来，而不是从事件或 item 上读回来：等结果的 sink 不能依赖「有人订阅了 Failed」。
        var outcome = CommandOutcome.Completed;
        Exception? failure = null;

        try
        {
            if (_isCtsNeeded)
            {
                await _command(item.Parameter, (item.Cts ?? new()).Token).ConfigureAwait(false);
            }
            else
            {
                await _command(item.Parameter, _defct).ConfigureAwait(false);
            }
            RaiseCommandEventAs(Completed, item, CommandEventType.Completed);
        }
        catch (OperationCanceledException)
        {
            outcome = CommandOutcome.Canceled;
            RaiseCommandEventAs(Canceled, item, CommandEventType.Canceled);
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
                item.Complete(outcome, failure);
                item.TakeCts()?.Dispose();
            }
        }
    }

    private async Task OnExecutionCompletedAsync(CommandEventArgs completed)
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
    public async Task UnLockAsync()
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
        List<CommandEventArgs> activeToCancel = [];

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
                // UnLockAsync，命令就永久锁死，此后每个排队项都再也跑不起来。
            }

            // 不给 sink 收尾：这一项的命令体还会自己走完 ExecuteCoreAsync 的 finally。
            RaiseCommandEventAs(Canceled, it, CommandEventType.Canceled);
        }

        if (!wasLocked)
        {
            await UnLockAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        List<CommandEventArgs> activeToCancel = [];
        List<CommandEventArgs> pendingToCancel = [];

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
            RaiseCommandEventAs(Canceled, it, CommandEventType.Canceled);
            it.TakeCts()?.Dispose();
            it.Complete(CommandOutcome.Canceled, null);
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
                // 与 InterruptAsync 同理：回调抛异常不能逃逸，否则 UnLockAsync 被跳过、命令永久锁死。
            }

            RaiseCommandEventAs(Canceled, it, CommandEventType.Canceled);
        }

        if (!wasLocked)
        {
            await UnLockAsync().ConfigureAwait(false);
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
        List<CommandEventArgs> toStart = [];

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
}

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
public sealed class CommandEventArgs(
    object? parameter,
    CommandEventType type,
    Exception? ex = null,
    CancellationTokenSource? cts = null)
{
    private CancellationTokenSource? _cts = cts;

    /// <summary>The argument passed to <see cref="System.Windows.Input.ICommand.Execute"/>.</summary>
    public object? Parameter { get; } = parameter;

    /// <summary>The failure that ended the execution, on <see cref="CommandEventType.Failed"/> only.</summary>
    public Exception? Exception { get; } = ex;

    /// <summary>The lifecycle stage this instance reports.</summary>
    public CommandEventType EventType { get; } = type;

    /// <summary>
    /// The cancellation source of this execution, or <see langword="null"/> for a command whose body never
    /// receives a token.
    /// </summary>
    /// <remarks>
    /// The command releases this source once the execution is over, so it is only usable while a body is in
    /// flight. <c>Interrupt</c> and <c>Clear</c> cancel through it; nothing outside the command takes it.
    /// </remarks>
    public CancellationTokenSource? Cts
    {
        get => _cts;
        internal set => _cts = value;
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

    // 恰好收尾一次。没有等的人在时是空操作。
    internal void Complete(CommandOutcome outcome, Exception? exception)
        => Completion?.TrySetResult(new CommandCompletion(outcome, exception));

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
