using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM;

public enum CommandEventType : int
{
    None = 0,
    Created,   // 已创建
    Enqueued,  // 入队等待
    Dequeued,  // 出队准备执行
    Started,   // 实际开始执行
    Completed, // 执行成功
    Failed,    // 执行失败
    Canceled,  // 被取消
    Exited     // 生命周期结束
}

public sealed class VeloxCommand(Func<object?, CancellationToken, Task> command,
                    Predicate<object?>? canExecute = null,
                    int semaphore = 1) : IVeloxCommand
{
    public static VeloxCommand CreateTaskOnlyWithParameter(
        Func<object?, Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new((parameter, _) => { command(parameter); return Task.CompletedTask; },
            canExecute,
            semaphore);

    public static VeloxCommand CreateTaskOnlyWithCancellationToken(
        Func<CancellationToken, Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            (_, ct) => { command(ct); return Task.CompletedTask; },
            canExecute,
            semaphore);

    public VeloxCommand(
        Func<Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : this(
            (_, __) => command(),
            canExecute,
            semaphore)
    {
        _isCtsNeeded = false;
    }

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

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Queue<CommandEventArgs> _pendingQueue = new();
    private readonly List<CommandEventArgs> _active = [];

    private int _maxConcurrency = Math.Max(1, semaphore);
    private bool _isForceLocked = false;

    private readonly bool _isCtsNeeded = true;
    private static readonly CancellationToken _defct = new();

    public event EventHandler? CanExecuteChanged;

    public event CommandEventHandler? Created;
    public event CommandEventHandler? Enqueued;
    public event CommandEventHandler? Dequeued;
    public event CommandEventHandler? Started;
    public event CommandEventHandler? Completed;
    public event CommandEventHandler? Failed;
    public event CommandEventHandler? Canceled;
    public event CommandEventHandler? Exited;

    public bool CanExecute(object? parameter)
        => (_canExecute?.Invoke(parameter) ?? true) && !_isForceLocked;

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public void Lock() => _ = LockAsync();
    public void UnLock() => _ = UnLockAsync();
    public void Interrupt() => _ = InterruptAsync();
    public void Clear() => _ = ClearAsync();
    public void Continue() => _ = ContinueAsync();
    public void ChangeSemaphore(int s) => _ = ChangeSemaphoreAsync(s);

    public async Task ExecuteAsync(object? parameter)
    {
        var item = new CommandEventArgs(parameter, CommandEventType.Created);
        if (_isCtsNeeded)
        {
            item.Cts = new();
        }
        Created?.Invoke(item);

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isForceLocked)
            {
                item.Cts?.Cancel();
                Canceled?.Invoke(item.With(CommandEventType.Canceled));
                return;
            }

            if (_active.Count < _maxConcurrency)
            {
                _active.Add(item);
                _ = ExecuteCoreAsync(item);
            }
            else
            {
                _pendingQueue.Enqueue(item);
                Enqueued?.Invoke(item.With(CommandEventType.Enqueued));
            }
        }
        finally
        {
            _stateLock.Release();
            Notify();
        }
    }

    private async Task ExecuteCoreAsync(CommandEventArgs item)
    {
        Started?.Invoke(item.With(CommandEventType.Started));

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
            Completed?.Invoke(item.With(CommandEventType.Completed));
        }
        catch (OperationCanceledException)
        {
            Canceled?.Invoke(item.With(CommandEventType.Canceled));
        }
        catch (Exception ex)
        {
            Failed?.Invoke(item.With(CommandEventType.Failed, ex));
        }
        finally
        {
            await OnExecutionCompletedAsync(item).ConfigureAwait(false);
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

        Exited?.Invoke(completed.With(CommandEventType.Exited));
        Notify();

        await TryStartPendingAsync().ConfigureAwait(false);
    }

    public async Task LockAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _isForceLocked = true;
        }
        finally
        {
            _stateLock.Release();
        }

        Notify();
    }

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

    public async Task InterruptAsync()
    {
        List<CommandEventArgs> activeToCancel = [];

        await LockAsync().ConfigureAwait(false);

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
            it.Cts?.Cancel();
            Canceled?.Invoke(it.With(CommandEventType.Canceled));
        }

        await UnLockAsync().ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        List<CommandEventArgs> activeToCancel = [];
        List<CommandEventArgs> pendingToCancel = [];

        await LockAsync().ConfigureAwait(false);

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            activeToCancel.AddRange(_active);
            _active.Clear();

            while (_pendingQueue.Count > 0)
            {
                var item = _pendingQueue.Dequeue();
                Dequeued?.Invoke(item.With(CommandEventType.Dequeued));
                pendingToCancel.Add(item);
            }
        }
        finally
        {
            _stateLock.Release();
        }

        foreach (var it in pendingToCancel.Concat(activeToCancel))
        {
            it.Cts?.Cancel();
            Canceled?.Invoke(it.With(CommandEventType.Canceled));
        }

        await UnLockAsync().ConfigureAwait(false);
    }

    public async Task ContinueAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isForceLocked)
                return;
        }
        finally
        {
            _stateLock.Release();
        }

        await TryStartPendingAsync().ConfigureAwait(false);
    }

    public async Task ChangeSemaphoreAsync(int semaphore)
    {
        if (semaphore < 1)
            return;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _maxConcurrency = Math.Max(1, semaphore);
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
            Dequeued?.Invoke(next.With(CommandEventType.Dequeued));
            _ = ExecuteCoreAsync(next);
        }

        Notify();
    }
}

public delegate void CommandEventHandler(CommandEventArgs e);

public sealed class CommandEventArgs(
    object? parameter,
    CommandEventType type,
    Exception? ex = null,
    CancellationTokenSource? cts = null)
{
    public object? Parameter { get; } = parameter;
    public Exception? Exception { get; } = ex;
    public CommandEventType EventType { get; } = type;
    public CancellationTokenSource? Cts { get; internal set; } = cts;

    public CommandEventArgs With(CommandEventType newType, Exception? ex = null)
        => new(Parameter, newType, ex ?? Exception, Cts);
}