using System.Windows.Input;

namespace VeloxDev.Core.Interfaces.MVVM
{
    public interface IVeloxCommand : ICommand
    {
        public void Notify();
        public void Cancel();
        public void Interrupt();
        public bool IsExecuting { get; }
    }
}
