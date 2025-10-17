using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

public partial class LinkViewModel : IWorkflowLinkViewModel
{
    private IWorkflowLinkViewModelHelper Helper = new WorkflowHelper.ViewModel.Link();

    [VeloxProperty] private IWorkflowSlotViewModel? _sender;
    [VeloxProperty] private IWorkflowSlotViewModel? _receiver;

    [VeloxCommand]
    private async Task Delete(object? parameter, CancellationToken ct)
    {
        await Helper.DeleteAsync();
    }

    public async Task InitializeAsync()
    {
        await Helper.InitializeAsync(this);
    }

    public async Task CloseAsync()
    {
        await Helper.CloseAsync();
    }

    public IWorkflowLinkViewModelHelper GetHelper() => Helper;
    public async Task SetHelperAsync(IWorkflowLinkViewModelHelper helper)
    {
        Helper = helper;
        await helper.InitializeAsync(this);
    }
}