using System.Windows.Input;

namespace VeloxDev.Core.Interfaces.MVVM
{
    public interface IVeloxCommand : ICommand
    {
        public void Lock();
        public void UnLock();
        public void Notify();
        public void Cancel();
        public void Interrupt();
        public Task ExecuteAsync(object? parameter);
        public Task CancelAsync();
        public Task InterruptAsync();
    }
}
