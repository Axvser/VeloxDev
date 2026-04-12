using System.ComponentModel;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem
{
    public interface IWorkflowViewModel : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public void InitializeWorkflow();
        public void OnPropertyChanging(string propertyName);
        public void OnPropertyChanged(string propertyName);

        public IVeloxCommand CloseCommand { get; }
    }
}
