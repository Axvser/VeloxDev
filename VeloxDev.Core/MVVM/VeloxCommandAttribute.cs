using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// Task ➤ Command
    /// <para><strong>Format : </strong> <c>Task MethodName(object? parameter, CancellationToken ct)</c></para>
    /// </summary>
    /// <param name="Name">The name of the command, if not specified, it will be automatically generated</param>
    /// <param name="CanValidate">True indicates that the executability verification of this command is enabled</param>
    /// <param name="CanConcurrent">True indicates that the command is concurrent</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class VeloxCommandAttribute(string Name = "Auto", bool CanValidate = false, bool CanConcurrent = false) : Attribute
    {
        public string Name { get; } = Name;
        public bool CanValidate { get; } = CanValidate;
        public bool CanConcurrent { get; } = CanConcurrent;
    }

    public sealed class ConcurrentVeloxCommand(
        Func<object?, CancellationToken, Task> executeAsync,
        Predicate<object?>? canExecute = null) : IVeloxCommand
    {
        private readonly Func<object?, CancellationToken, Task> _executeAsync = executeAsync;
        private readonly List<CancellationTokenSource> _activeExecutions = [];
        private readonly SemaphoreSlim _asyncLock = new(1, 1);

        public event EventHandler? CanExecuteChanged;
        public bool IsExecuting { get; private set; }

        public bool CanExecute(object? parameter)
        {
            return canExecute?.Invoke(parameter) ?? true;
        }
        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }
        public async void Cancel()
        {
            await CancelCurrentAsync();
        }
        public async void Interrupt()
        {
            await InterruptAsync();
        }
        public void OnCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        public async Task ExecuteAsync(object? parameter)
        {
            var cts = new CancellationTokenSource();

            await _asyncLock.WaitAsync();
            try
            {
                _activeExecutions.Add(cts);
                IsExecuting = true;
            }
            finally
            {
                _asyncLock.Release();
            }

            try
            {
                await _executeAsync.Invoke(parameter, cts.Token);
            }
            catch
            {

            }
            finally
            {
                await _asyncLock.WaitAsync();
                try
                {
                    _activeExecutions.Remove(cts);
                    IsExecuting = _activeExecutions.Count > 0;
                }
                finally
                {
                    _asyncLock.Release();
                }
            }
        }
        public async Task CancelCurrentAsync()
        {
            List<CancellationTokenSource> executionsToCancel;

            await _asyncLock.WaitAsync();
            try
            {
                executionsToCancel = [.. _activeExecutions];
                _activeExecutions.Clear();
                IsExecuting = false;
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
        public Task InterruptAsync() => CancelCurrentAsync();
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

        public event EventHandler? CanExecuteChanged;
        public bool IsExecuting => _activeExecutionCount > 0;

        public bool CanExecute(object? parameter)
        {
            return canExecute?.Invoke(parameter) ?? true;
        }
        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }
        public async void Cancel()
        {
            await CancelCurrentAsync();
        }
        public async void Interrupt()
        {
            await InterruptAsync();
        }
        public void OnCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        public async Task ExecuteAsync(object? parameter)
        {
            if (_isInterrupted)
            {
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingExecutions.Enqueue(tcs);

            await _queueSemaphore.WaitAsync();
            try
            {
                if (_isInterrupted)
                {
                    TrySetResultSafe(tcs, false);
                    return;
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
        public async Task CancelCurrentAsync()
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

                // 安全地清空队列
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