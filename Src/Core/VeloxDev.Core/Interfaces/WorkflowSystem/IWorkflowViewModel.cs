using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowViewModel : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public void InitializeWorkflow();
        public void OnPropertyChanging(string propertyName);
        public void OnPropertyChanged(string propertyName);

        public IVeloxCommand CloseCommand { get; }
    }
}
