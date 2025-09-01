using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// Task ➤ Command
    /// <para><strong>Format : </strong> <c>Task MethodName(object? parameter, CancellationToken ct)</c></para>
    /// </summary>
    /// <param name="name">The name of the command, if not specified, it will be automatically generated</param>
    /// <param name="canValidate">True indicates that the executability verification of this command is enabled</param>
    /// <param name="semaphore">Concurrent Capacity</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class VeloxCommandAttribute(
        string name = "Auto",
        bool canValidate = false,
        int semaphore = 1) : Attribute
    {
        public string Name { get; } = name;
        public bool CanValidate { get; } = canValidate;
        public int Semaphore { get; } = semaphore;
    }

    public sealed class ConcurrentVeloxCommand(
        Func<object?, CancellationToken, Task> executeAsync,
        Predicate<object?>? canExecute = null,
        int semaphore = 1) : IVeloxCommand
    {
        private readonly Func<object?, CancellationToken, Task> _executeAsync = executeAsync;
        private readonly List<CancellationTokenSource> _activeExecutions = [];
        private readonly SemaphoreSlim _asyncLock = new(semaphore, semaphore);
        private bool _isForceLocked = false;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return (canExecute?.Invoke(parameter) ?? true) && !_isForceLocked;
        }

        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }

        public void Notify()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public async void Cancel()
        {
            await CancelAsync();
        }

        public async void Interrupt()
        {
            await InterruptAsync();
        }

        public async Task ExecuteAsync(object? parameter)
        {
            await _asyncLock.WaitAsync();
            try
            {
                if (_isForceLocked)
                {
                    var canceledCts = new CancellationTokenSource();
                    canceledCts.Cancel();
                    await _executeAsync.Invoke(parameter, canceledCts.Token);
                    return;
                }

                var cts = new CancellationTokenSource();
                _activeExecutions.Add(cts);

                try
                {
                    await _executeAsync.Invoke(parameter, cts.Token);
                }
                finally
                {
                    _activeExecutions.Remove(cts);
                }
            }
            finally
            {
                _asyncLock.Release();
            }
        }

        public async Task CancelAsync()
        {
            List<CancellationTokenSource> executionsToCancel;

            await _asyncLock.WaitAsync();
            try
            {
                executionsToCancel = [.. _activeExecutions];
                _activeExecutions.Clear();
            }
            finally
            {
                _asyncLock.Release();
            }

            foreach (var cts in executionsToCancel)
            {
                cts.Cancel();
            }
        }

        public async Task InterruptAsync() => await CancelAsync();

        public void Lock()
        {
            _asyncLock.Wait();
            try
            {
                _isForceLocked = true;
                var executionsToCancel = new List<CancellationTokenSource>(_activeExecutions);
                _activeExecutions.Clear();

                foreach (var cts in executionsToCancel)
                {
                    cts.Cancel();
                }
            }
            finally
            {
                _asyncLock.Release();
            }

            Notify();
        }

        public void UnLock()
        {
            _isForceLocked = false;
            Notify();
        }
    }

    public sealed class VeloxCommand(
        Func<object?, CancellationToken, Task> executeAsync,
        Predicate<object?>? canExecute = null) : IVeloxCommand
    {
        private readonly SemaphoreSlim _queueSemaphore = new(1, 1);
        private readonly Func<object?, CancellationToken, Task> _executeAsync = executeAsync;
        private CancellationTokenSource? _currentExecutionCts = null;
        private readonly Queue<TaskCompletionSource<bool>> _pendingExecutions = new();
        private bool _isInterrupted = false;
        private int _activeExecutionCount = 0;
        private bool _isForceLocked = false;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return (canExecute?.Invoke(parameter) ?? true) && !_isForceLocked;
        }

        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }

        public void Notify()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public async void Cancel()
        {
            await CancelAsync();
        }

        public async void Interrupt()
        {
            await InterruptAsync();
        }

        public async Task ExecuteAsync(object? parameter)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await _queueSemaphore.WaitAsync();
            try
            {
                if (_isForceLocked)
                {
                    var canceledCts = new CancellationTokenSource();
                    canceledCts.Cancel();
                    await _executeAsync.Invoke(parameter, canceledCts.Token);
                    TrySetResultSafe(tcs, false);
                    return;
                }

                _pendingExecutions.Enqueue(tcs);

                if (_isInterrupted)
                {
                    TrySetResultSafe(tcs, false);
                    throw new TaskCanceledException();
                }

                Interlocked.Increment(ref _activeExecutionCount);
                _currentExecutionCts?.Cancel();
                _currentExecutionCts = new CancellationTokenSource();

                while (_pendingExecutions.Count > 0)
                {
                    var pendingTask = _pendingExecutions.Dequeue();
                    TrySetResultSafe(pendingTask, false);
                }

                try
                {
                    await _executeAsync.Invoke(parameter, _currentExecutionCts.Token);
                    TrySetResultSafe(tcs, true);
                }
                catch
                {
                    TrySetResultSafe(tcs, false);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeExecutionCount);
                }
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }

        public async Task CancelAsync()
        {
            await _queueSemaphore.WaitAsync();
            try
            {
                _currentExecutionCts?.Cancel();
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }

        public async Task InterruptAsync()
        {
            await _queueSemaphore.WaitAsync();
            try
            {
                _isInterrupted = true;
                _currentExecutionCts?.Cancel();

                while (_pendingExecutions.Count > 0)
                {
                    var pendingTask = _pendingExecutions.Dequeue();
                    TrySetResultSafe(pendingTask, false);
                }

                _isInterrupted = false;
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }

        public void Lock()
        {
            _queueSemaphore.Wait();
            try
            {
                _isForceLocked = true;
                _currentExecutionCts?.Cancel();

                while (_pendingExecutions.Count > 0)
                {
                    var pendingTask = _pendingExecutions.Dequeue();
                    TrySetResultSafe(pendingTask, false);
                }
            }
            finally
            {
                _queueSemaphore.Release();
            }

            Notify();
        }

        public void UnLock()
        {
            _isForceLocked = false;
            Notify();
        }

        private static void TrySetResultSafe(TaskCompletionSource<bool> tcs, bool result)
        {
            try
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult(result);
                }
            }
            catch
            {
            }
        }
    }
}