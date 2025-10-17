using System.ComponentModel;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowViewModel : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public Task InitializeAsync();
        public Task CloseAsync();
    }
}
