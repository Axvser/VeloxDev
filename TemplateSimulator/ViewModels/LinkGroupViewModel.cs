using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels
{
    public partial class LinkGroupViewModel : IWorkflowLinkGroupViewModel
    {
        private IWorkflowLinkGroupViewModelHelper Helper = new WorkflowHelper.ViewModel.LinkGroup();

        [VeloxProperty] private IWorkflowTreeViewModel? parent = null;
        [VeloxProperty] private ObservableCollection<IWorkflowLinkViewModel> links = [];

        [VeloxCommand]
        private async Task Delete(object? parameter, CancellationToken ct)
        {
            await Helper.DeleteAsync();
        }

        public async Task CloseAsync()
        {
            await Helper.CloseAsync();
        }

        public async Task InitializeAsync()
        {
            await Helper.InitializeAsync(this);
        }

        public IWorkflowLinkGroupViewModelHelper GetHelper() => Helper;
        public async Task SetHelperAsync(IWorkflowLinkGroupViewModelHelper helper)
        {
            Helper = helper;
            await helper.InitializeAsync(this);
        }
    }
}
