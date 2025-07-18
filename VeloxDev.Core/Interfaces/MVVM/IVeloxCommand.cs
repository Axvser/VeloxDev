using System.Windows.Input;

namespace VeloxDev.Core.Interfaces.MVVM
{
    public interface IVeloxCommand : ICommand
    {
        Task ExecuteAsync(object? parameter);
    }
}
