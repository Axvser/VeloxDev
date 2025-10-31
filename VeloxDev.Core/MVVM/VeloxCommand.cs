using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM
{
    public enum VeloxCommandEventType : int
    {
        None = 0,
        Created,   // 任务已创建
        Enqueued,  // 入队等待
        Dequeued,  // 出队准备执行
        Started,   // 实际开始执行
        Completed, // 执行成功
        Failed,    // 执行失败
        Canceled,  // 被取消
        Exited     // 生命周期结束
    }

    public sealed class VeloxCommand(Func<object?, CancellationToken, Task> executeAsync,
                        Predicate<object?>? canExecute = null,
                        int semaphore = 1) : IVeloxCommand
    {
        private readonly Func<object?, CancellationToken, Task> _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        private readonly Predicate<object?>? _canExecute = canExecute;

        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private readonly Queue<VeloxCommandEventArgs> _pendingQueue = new();
        private readonly List<VeloxCommandEventArgs> _active = [];

        private int _maxConcurrency = Math.Max(1, semaphore);
        private bool _isForceLocked = false;

        public event EventHandler? CanExecuteChanged;

        public event VeloxCommandEventHandler? TaskCreated;
        public event VeloxCommandEventHandler? TaskEnqueued;
        public event VeloxCommandEventHandler? TaskDequeued;
        public event VeloxCommandEventHandler? TaskStarted;
        public event VeloxCommandEventHandler? TaskCompleted;
        public event VeloxCommandEventHandler? TaskFailed;
        public event VeloxCommandEventHandler? TaskCanceled;
        public event VeloxCommandEventHandler? TaskExited;

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
            var item = new VeloxCommandEventArgs(parameter, VeloxCommandEventType.Created);
            TaskCreated?.Invoke(item);

            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isForceLocked)
                {
                    item.Cts.Cancel();
                    TaskCanceled?.Invoke(item.With(VeloxCommandEventType.Canceled));
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
                    TaskEnqueued?.Invoke(item.With(VeloxCommandEventType.Enqueued));
                }
            }
            finally
            {
                _stateLock.Release();
                Notify();
            }
        }

        private async Task ExecuteCoreAsync(VeloxCommandEventArgs item)
        {
            TaskStarted?.Invoke(item.With(VeloxCommandEventType.Started));

            try
            {
                await _executeAsync(item.Parameter, item.Cts.Token).ConfigureAwait(false);
                TaskCompleted?.Invoke(item.With(VeloxCommandEventType.Completed));
            }
            catch (OperationCanceledException)
            {
                TaskCanceled?.Invoke(item.With(VeloxCommandEventType.Canceled));
            }
            catch (Exception ex)
            {
                TaskFailed?.Invoke(item.With(VeloxCommandEventType.Failed, ex));
            }
            finally
            {
                await OnExecutionCompletedAsync(item).ConfigureAwait(false);
            }
        }

        private async Task OnExecutionCompletedAsync(VeloxCommandEventArgs completed)
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

            TaskExited?.Invoke(completed.With(VeloxCommandEventType.Exited));
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
            List<VeloxCommandEventArgs> activeToCancel = [];

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
                it.Cts.Cancel();
                TaskCanceled?.Invoke(it.With(VeloxCommandEventType.Canceled));
            }

            await UnLockAsync().ConfigureAwait(false);
        }

        public async Task ClearAsync()
        {
            List<VeloxCommandEventArgs> activeToCancel = [];
            List<VeloxCommandEventArgs> pendingToCancel = [];

            await LockAsync().ConfigureAwait(false);

            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                activeToCancel.AddRange(_active);
                _active.Clear();

                while (_pendingQueue.Count > 0)
                {
                    var item = _pendingQueue.Dequeue();
                    TaskDequeued?.Invoke(item.With(VeloxCommandEventType.Dequeued));
                    pendingToCancel.Add(item);
                }
            }
            finally
            {
                _stateLock.Release();
            }

            foreach (var it in pendingToCancel.Concat(activeToCancel))
            {
                it.Cts.Cancel();
                TaskCanceled?.Invoke(it.With(VeloxCommandEventType.Canceled));
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
            List<VeloxCommandEventArgs> toStart = [];

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
                TaskDequeued?.Invoke(next.With(VeloxCommandEventType.Dequeued));
                _ = ExecuteCoreAsync(next);
            }

            Notify();
        }
    }

    public delegate void VeloxCommandEventHandler(VeloxCommandEventArgs e);

    public sealed class VeloxCommandEventArgs(object? parameter, VeloxCommandEventType type, Exception? ex = null)
    {
        public object? Parameter { get; } = parameter;
        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        public Exception? Exception { get; } = ex;
        public VeloxCommandEventType EventType { get; } = type;

        public VeloxCommandEventArgs With(VeloxCommandEventType newType, Exception? ex = null)
            => new(Parameter, newType, ex ?? Exception) { };
    }
}