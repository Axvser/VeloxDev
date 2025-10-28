using System.Windows.Input;

namespace VeloxDev.Core.Interfaces.MVVM
{
    /// <summary>
    /// Command model supporting task queuing and concurrent execution management
    /// </summary>
    public interface IVeloxCommand : ICommand
    {
        /// <summary>
        /// Invoked when command execution starts
        /// </summary>
        public event EventHandler<object?>? ExecutionStarted;

        /// <summary>
        /// Invoked when command execution completes successfully
        /// </summary>
        public event EventHandler<object?>? ExecutionCompleted;

        /// <summary>
        /// Invoked when command execution fails with an exception
        /// </summary>
        public event EventHandler<(object? Parameter, Exception Exception)>? ExecutionFailed;

        /// <summary>
        /// Invoked when a task is added to the pending queue
        /// </summary>
        public event EventHandler<object?>? TaskEnqueued;

        /// <summary>
        /// Invoked when a task is removed from the pending queue for execution
        /// </summary>
        public event EventHandler<object?>? TaskDequeued;

        /// <summary>
        /// Invoked when a CancellationTokenSource is created for a new execution item
        /// </summary>
        public event EventHandler<CancellationTokenSource>? TokenSourceCreated;

        /// <summary>
        /// Locks the command to prevent new task executions (asynchronous fire-and-forget)
        /// </summary>
        public void Lock();

        /// <summary>
        /// Unlocks the command to allow new task executions (asynchronous fire-and-forget)
        /// </summary>
        public void UnLock();

        /// <summary>
        /// Notifies the command to re-evaluate its execution state
        /// </summary>
        public void Notify();

        /// <summary>
        /// Cancels all executing tasks and clears the entire pending queue (asynchronous fire-and-forget)
        /// </summary>
        public void Clear();

        /// <summary>
        /// Cancels all currently executing tasks while preserving the pending queue (asynchronous fire-and-forget)
        /// </summary>
        public void Interrupt();

        /// <summary>
        /// Attempts to start execution of pending tasks if conditions permit (asynchronous fire-and-forget)
        /// </summary>
        public void Continue();

        /// <summary>
        /// Modifies the command's semaphore count for concurrent task control (asynchronous fire-and-forget)
        /// </summary>
        /// <param name="semaphore">New semaphore value (minimum 1)</param>
        public void ChangeSemaphore(int semaphore);

        /// <summary>
        /// Executes the command asynchronously with parameter support
        /// </summary>
        /// <param name="parameter">Command execution parameter</param>
        public Task ExecuteAsync(object? parameter);

        /// <summary>
        /// Locks the command asynchronously to prevent new task executions
        /// </summary>
        Task LockAsync();

        /// <summary>
        /// Unlocks the command asynchronously to allow new task executions
        /// </summary>
        Task UnLockAsync();

        /// <summary>
        /// Cancels all executing tasks and clears the entire pending queue asynchronously
        /// </summary>
        public Task ClearAsync();

        /// <summary>
        /// Cancels all currently executing tasks while preserving the pending queue asynchronously
        /// </summary>
        public Task InterruptAsync();

        /// <summary>
        /// Attempts to start execution of pending tasks if conditions permit asynchronously
        /// </summary>
        public Task ContinueAsync();

        /// <summary>
        /// Modifies the command's semaphore count for concurrent task control asynchronously
        /// </summary>
        /// <param name="semaphore">New semaphore value (minimum 1)</param>
        public Task ChangeSemaphoreAsync(int semaphore);
    }
}