using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM
{
    public sealed class VeloxCommand(Func<object?, CancellationToken, Task> executeAsync,
                        Predicate<object?>? canExecute = null,
                        int semaphore = 1) : IVeloxCommand
    {
        private readonly Func<object?, CancellationToken, Task> _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        private readonly Predicate<object?>? _canExecute = canExecute;

        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private readonly Queue<ExecutionItem> _pendingQueue = new();
        private readonly List<ExecutionItem> _active = [];

        private int _maxConcurrency = Math.Max(1, semaphore);
        private bool _isForceLocked = false;

        public event EventHandler<CancellationTokenSource>? TokenSourceCreated;

        public event EventHandler? CanExecuteChanged;
        public event EventHandler<object?>? ExecutionStarted;
        public event EventHandler<object?>? ExecutionCompleted;
        public event EventHandler<(object? Parameter, Exception Exception)>? ExecutionFailed;

        public event EventHandler<object?>? TaskEnqueued;
        public event EventHandler<object?>? TaskDequeued;

        public sealed class ExecutionItem(object? p)
        {
            public object? Parameter { get; } = p;
            public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        }

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
            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var item = new ExecutionItem(parameter);
                TokenSourceCreated?.Invoke(this, item.Cts);

                // If externally force locked, reject new tasks.
                if (_isForceLocked)
                    return;

                if (_active.Count < _maxConcurrency)
                {
                    _active.Add(item);
                    _ = ExecuteCoreAsync(item);
                    return;
                }

                _pendingQueue.Enqueue(item);
                TaskEnqueued?.Invoke(this, item);
            }
            finally
            {
                _stateLock.Release();
            }
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

            // Notify outside lock to avoid UI handler reentrancy holding our lock.
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
            List<ExecutionItem> toCancel = [];

            // step 1: lock to prevent new tasks entering
            await LockAsync().ConfigureAwait(false);

            // step 2: snapshot current active and remove them from active (atomic)
            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                toCancel.AddRange(_active);
                _active.Clear();
            }
            finally
            {
                _stateLock.Release();
            }

            // step 3: cancel tokens outside lock
            foreach (var it in toCancel)
                it.Cts.Cancel();

            // step 4: unlock (allow new tasks and potentially re-schedule pending)
            await UnLockAsync().ConfigureAwait(false);
        }
        public async Task ClearAsync()
        {
            List<ExecutionItem> toCancel = [];

            // step 1: lock
            await LockAsync().ConfigureAwait(false);

            // step 2: snapshot active and pending, then clear them (atomic)
            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                toCancel.AddRange(_active);
                _active.Clear();

                while (_pendingQueue.Count > 0)
                {
                    var item = _pendingQueue.Dequeue();
                    TaskDequeued?.Invoke(this, item);
                    toCancel.Add(item);
                }
            }
            finally
            {
                _stateLock.Release();
            }

            // step 3: cancel outside lock
            foreach (var it in toCancel)
                it.Cts.Cancel();

            // step 4: unlock
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
            if (semaphore < 1) return;

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

        private async Task ExecuteCoreAsync(ExecutionItem item)
        {
            try
            {
                ExecutionStarted?.Invoke(this, item.Parameter);
                await _executeAsync(item.Parameter, item.Cts.Token).ConfigureAwait(false);
                ExecutionCompleted?.Invoke(this, item.Parameter);
            }
            catch (Exception ex)
            {
                // 汇报失败，但不 rethrow（防止未观察的异常破坏线程）
                ExecutionFailed?.Invoke(this, (item.Parameter, ex));
            }
            finally
            {
                await OnExecutionCompletedAsync(item).ConfigureAwait(false);
            }
        }
        private async Task OnExecutionCompletedAsync(ExecutionItem completed)
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

            await TryStartPendingAsync().ConfigureAwait(false);
        }
        private async Task TryStartPendingAsync()
        {
            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                while (_pendingQueue.Count > 0 &&
                       _active.Count < _maxConcurrency &&
                       !_isForceLocked)
                {
                    var next = _pendingQueue.Dequeue();
                    TaskDequeued?.Invoke(this, next);
                    _active.Add(next);
                    _ = ExecuteCoreAsync(next);
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }
    }
}