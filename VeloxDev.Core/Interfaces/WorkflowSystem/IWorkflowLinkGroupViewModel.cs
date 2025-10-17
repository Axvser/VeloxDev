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
        public Task SetHelperAsync(IWorkflowLinkGroupViewModelHelper helper);
    }

    public interface IWorkflowLinkGroupViewModelHelper : IDisposable
    {
        public Task InitializeAsync(IWorkflowLinkGroupViewModel viewModel);
        public Task CloseAsync();
        public Task DeleteAsync();
    }
}
