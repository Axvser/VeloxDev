using System.Windows.Input;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.Interfaces.MVVM
{
    public interface IVeloxCommand : ICommand
    {
        public event CommandEventHandler? Created;
        public event CommandEventHandler? Started;
        public event CommandEventHandler? Completed;
        public event CommandEventHandler? Canceled;
        public event CommandEventHandler? Failed;
        public event CommandEventHandler? Exited;
        public event CommandEventHandler? Enqueued;
        public event CommandEventHandler? Dequeued;

        public void Lock();
        public void UnLock();
        public void Notify();
        public void Clear();
        public void Interrupt();
        public void Continue();
        public void ChangeSemaphore(int semaphore);

        public Task ExecuteAsync(object? parameter);
        Task LockAsync();
        Task UnLockAsync();
        public Task ClearAsync();
        public Task InterruptAsync();
        public Task ContinueAsync();
        public Task ChangeSemaphoreAsync(int semaphore);
    }
}