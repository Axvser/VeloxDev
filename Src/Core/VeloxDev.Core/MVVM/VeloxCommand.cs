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
/// Nothing here marshals to a UI thread, and event subscribers run on whatever thread the pipeline happens to
/// be on. A handler that touches UI must dispatch back itself. A subscriber that throws does not disturb the
/// command; subscribe to <see cref="HandlerException"/> to observe such failures.
/// </para>
/// </remarks>
public sealed class VeloxCommand(Func<object?, CancellationToken, Task> command,
                    Predicate<object?>? canExecute = null,
                    int semaphore = 1) : IVeloxCommand
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

    private void RaiseCanExecuteChanged()
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

    private static void RaiseCommandEvent(CommandEventHandler? handler, CommandEventArgs args)
    {
        try
        {
            handler?.Invoke(args);
        }
        catch (Exception ex)
        {
            ReportHandlerException(ex);
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
    public async Task ExecuteAsync(object? parameter)
    {
        var item = new CommandEventArgs(parameter, CommandEventType.Created);
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
            RaiseCommandEvent(Canceled, item.With(CommandEventType.Canceled));
            // 这条路径不经过 ExecuteCoreAsync，所以由这里负责释放。
            item.TakeCts()?.Dispose();
        }
        else if (enqueued)
        {
            RaiseCommandEvent(Enqueued, item.With(CommandEventType.Enqueued));
        }
        else if (startNow)
        {
            _ = ExecuteCoreAsync(item);
        }

        Notify();
    }

    private async Task ExecuteCoreAsync(CommandEventArgs item)
    {
        RaiseCommandEvent(Started, item.With(CommandEventType.Started));

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
            RaiseCommandEvent(Completed, item.With(CommandEventType.Completed));
        }
        catch (OperationCanceledException)
        {
            RaiseCommandEvent(Canceled, item.With(CommandEventType.Canceled));
        }
        catch (Exception ex)
        {
            RaiseCommandEvent(Failed, item.With(CommandEventType.Failed, ex));
        }
        finally
        {
            await OnExecutionCompletedAsync(item).ConfigureAwait(false);

            // 走到这里命令体一定已经结束 —— 无论它成功、失败还是被取消，也无论它是否已被
            // Interrupt/Clear 从 _active 摘走。所以这里是可以确定「没人再观察 token」的唯一位置。
            item.TakeCts()?.Dispose();
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

        RaiseCommandEvent(Exited, completed.With(CommandEventType.Exited));

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

            RaiseCommandEvent(Canceled, it.With(CommandEventType.Canceled));
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
            RaiseCommandEvent(Dequeued, it.With(CommandEventType.Dequeued));
        }

        foreach (var it in pendingToCancel)
        {
            // 排队项从未进入 ExecuteCoreAsync，所以它们的源只能在这里释放。
            it.Cts?.Cancel();
            RaiseCommandEvent(Canceled, it.With(CommandEventType.Canceled));
            it.TakeCts()?.Dispose();
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

            RaiseCommandEvent(Canceled, it.With(CommandEventType.Canceled));
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
            RaiseCommandEvent(Dequeued, next.With(CommandEventType.Dequeued));
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
