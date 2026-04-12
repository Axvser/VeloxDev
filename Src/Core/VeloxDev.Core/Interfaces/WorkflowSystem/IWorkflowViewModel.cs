using System.ComponentModel;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public interface IWorkflowViewModel : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public void InitializeWorkflow();
        public void OnPropertyChanging(string propertyName);
        public void OnPropertyChanged(string propertyName);

        public IVeloxCommand CloseCommand { get; }
    }
}
