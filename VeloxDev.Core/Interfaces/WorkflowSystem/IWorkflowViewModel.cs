using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowViewModel : INotifyPropertyChanging, INotifyPropertyChanged
    {
        public void InitializeWorkflow();

        public IVeloxCommand CloseCommand { get; }
    }
}
