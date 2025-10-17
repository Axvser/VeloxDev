using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowLinkGroupViewModel : IWorkflowViewModel
    {
        public IWorkflowTreeViewModel? Parent { get; set; }
        public ObservableCollection<IWorkflowLinkViewModel> Links { get; set; }

        public IVeloxCommand DeleteCommand { get; }
        
        public IWorkflowLinkGroupViewModelHelper GetHelper();
        public void SetHelper(IWorkflowLinkGroupViewModelHelper helper);
    }

    public interface IWorkflowLinkGroupViewModelHelper : IWorkflowHelper
    {
        public void Initialize(IWorkflowLinkGroupViewModel linkGroup);
        public void Delete();
    }
}
