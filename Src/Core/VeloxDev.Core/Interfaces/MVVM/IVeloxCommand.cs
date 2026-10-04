using System.Windows.Input;

namespace VeloxDev.MVVM
{
    /// <summary>A command that queues its executions, reports how each ended, and can be locked, interrupted and awaited.</summary>
    public interface IVeloxCommand : ICommand
    {
        /// <summary>Raised after a call has been accepted for execution.</summary>
        public event CommandEventHandler? Created;

        /// <summary>Raised when an execution starts running.</summary>
        public event CommandEventHandler? Started;

        /// <summary>Raised when an execution completes successfully.</summary>
        public event CommandEventHandler? Completed;

        /// <summary>Raised when an execution is cancelled.</summary>
        public event CommandEventHandler? Canceled;

        /// <summary>Raised when an execution fails with an exception.</summary>
        public event CommandEventHandler? Failed;

        /// <summary>Raised when an execution leaves the pipeline, however it ended.</summary>
        public event CommandEventHandler? Exited;

        /// <summary>Raised when an execution is queued waiting for a free slot.</summary>
        public event CommandEventHandler? Enqueued;

        /// <summary>Raised when a queued execution is taken from the queue.</summary>
        public event CommandEventHandler? Dequeued;

        /// <summary>Refuses new executions and parks the queue until <see cref="Unlock"/>.</summary>
        public void Lock();

        /// <summary>Re-enables the command and starts any queued work.</summary>
        public void Unlock();

        /// <summary>Raises <see cref="System.Windows.Input.ICommand.CanExecuteChanged"/> so bindings re-evaluate.</summary>
        public void Notify();

        /// <summary>Cancels every running execution and drops every queued one.</summary>
        public void Clear();

        /// <summary>Cancels the running executions, leaving the queue in place.</summary>
        public void Interrupt();

        /// <summary>Starts any queued work when the command is not locked.</summary>
        public void Continue();

        /// <summary>Changes the limit on concurrently running executions.</summary>
        /// <param name="semaphore">The new limit; must be at least 1.</param>
        public void ChangeSemaphore(int semaphore);

        /// <summary>Starts an execution and returns once the call has been accepted.</summary>
        /// <param name="parameter">The argument to pass to the body.</param>
        public Task ExecuteAsync(object? parameter);

        /// <summary>The awaitable counterpart of <see cref="Lock"/>.</summary>
        public Task LockAsync();

        /// <summary>The awaitable counterpart of <see cref="Unlock"/>.</summary>
        public Task UnlockAsync();

        /// <summary>The awaitable counterpart of <see cref="Clear"/>.</summary>
        public Task ClearAsync();

        /// <summary>The awaitable counterpart of <see cref="Interrupt"/>.</summary>
        public Task InterruptAsync();

        /// <summary>The awaitable counterpart of <see cref="Continue"/>.</summary>
        public Task ContinueAsync();

        /// <summary>The awaitable counterpart of <see cref="ChangeSemaphore"/>.</summary>
        /// <param name="semaphore">The new limit; must be at least 1.</param>
        public Task ChangeSemaphoreAsync(int semaphore);
    }
}
