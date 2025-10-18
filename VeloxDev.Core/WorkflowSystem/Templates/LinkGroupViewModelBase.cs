using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem.Templates
{
    public partial class LinkGroupViewModelBase : IWorkflowLinkGroupViewModel
    {
        private IWorkflowLinkGroupViewModelHelper Helper = new WorkflowHelper.ViewModel.LinkGroup();

        public LinkGroupViewModelBase() { InitializeWorkflow(); }

        [VeloxProperty] private IWorkflowTreeViewModel? parent = null;
        [VeloxProperty] private ObservableCollection<IWorkflowLinkViewModel> links = [];

        [VeloxCommand]
        protected virtual Task Delete(object? parameter, CancellationToken ct)
        {
            Helper.Delete();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual async Task Close(object? parameter, CancellationToken ct)
        {
            await Helper.CloseAsync();
        }

        public virtual IWorkflowLinkGroupViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper(IWorkflowLinkGroupViewModelHelper helper)
        {
            helper.Initialize(this);
            Helper = helper;
        }
    }
}
