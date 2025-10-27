using System.Windows.Input;

namespace VeloxDev.Core.Interfaces.MVVM
{
    /// <summary>
    /// Command model supporting task queuing and concurrent execution management
    /// </summary>
    public interface IVeloxCommand : ICommand
    {
        /// <summary>
        /// Invoked while execution started
        /// </summary>
        public event EventHandler<object?>? ExecutionStarted;

        /// <summary>
        /// Invoked while execution completed
        /// </summary>
        public event EventHandler<object?>? ExecutionCompleted;

        /// <summary>
        /// Invoked while execution failed
        /// </summary>
        public event EventHandler<(object? Parameter, Exception Exception)>? ExecutionFailed;

        /// <summary>
        /// Locks the command to prevent new task executions
        /// </summary>
        public void Lock();

        /// <summary>
        /// Unlocks the command to allow new task executions
        /// </summary>
        public void UnLock();

        /// <summary>
        /// Notifies the command to re-evaluate its execution state
        /// </summary>
        public void Notify();

        /// <summary>
        /// Clears the currently executing task
        /// </summary>
        public void Clear();

        /// <summary>
        /// Interrupts all tasks including queued operations
        /// </summary>
        public void Interrupt();

        /// <summary>
        /// Continues tasks in queue
        /// </summary>
        public void Continue();

        /// <summary>
        /// Modifies the command's semaphore count for concurrent task control
        /// </summary>
        /// <param name="semaphore">New semaphore value</param>
        public void ChangeSemaphore(int semaphore);

        /// <summary>
        /// Executes the command asynchronously with parameter support
        /// </summary>
        /// <param name="parameter">Command execution parameter</param>
        public Task ExecuteAsync(object? parameter);

        /// <summary>
        /// Clears the current execution state asynchronously
        /// </summary>
        public Task ClearAsync();

        /// <summary>
        /// Interrupts all command operations asynchronously
        /// </summary>
        public Task InterruptAsync();

        /// <summary>
        /// Continues tasks in queue asynchronously
        /// </summary>
        public Task ContinueAsync();

        /// <summary>
        /// Modifies the command's semaphore count asynchronously
        /// </summary>
        /// <param name="semaphore">New semaphore value</param>
        public Task ChangeSemaphoreAsync(int semaphore);
    }
}