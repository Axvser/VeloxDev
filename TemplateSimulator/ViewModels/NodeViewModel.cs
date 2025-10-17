using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

public partial class NodeViewModel : IWorkflowNodeViewModel
{
    private IWorkflowNodeViewModelHelper Helper = new WorkflowHelper.ViewModel.Node();

    [VeloxProperty] private IWorkflowTreeViewModel? parent = null;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];

    [VeloxCommand]
    private async Task CreateSlot(object? parameter, CancellationToken ct)
    {
        await Helper.CreateSlotAsync(parameter);
    }
    [VeloxCommand]
    private Task Work(object? parameter, CancellationToken ct)
    {
        // ºöÂÔ£¬ÓÉÎÒÇ××Ô±àÐ´
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Broadcast(object? parameter, CancellationToken ct)
    {
        // ºöÂÔ£¬ÓÉÎÒÇ××Ô±àÐ´
        return Task.CompletedTask;
    }
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

    public IWorkflowNodeViewModelHelper GetHelper() => Helper;
    public async Task SetHelperAsync(IWorkflowNodeViewModelHelper helper)
    {
        Helper = helper;
        await helper.InitializeAsync(this);
    }
}