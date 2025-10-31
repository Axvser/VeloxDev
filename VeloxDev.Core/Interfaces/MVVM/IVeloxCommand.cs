using System.Windows.Input;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.Interfaces.MVVM
{
    public interface IVeloxCommand : ICommand
    {
        public event VeloxCommandEventHandler? TaskCreated;
        public event VeloxCommandEventHandler? TaskStarted;
        public event VeloxCommandEventHandler? TaskCompleted;
        public event VeloxCommandEventHandler? TaskCanceled;
        public event VeloxCommandEventHandler? TaskFailed;
        public event VeloxCommandEventHandler? TaskExited;
        public event VeloxCommandEventHandler? TaskEnqueued;
        public event VeloxCommandEventHandler? TaskDequeued;
        
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