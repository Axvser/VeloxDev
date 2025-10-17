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
    private Task Press(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.Press(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Move(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.Move(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Scale(object? parameter, CancellationToken ct)
    {
        if (parameter is not Size size) return Task.CompletedTask;
        Helper.Scale(size);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Release(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.Release(anchor);
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task CreateSlot(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
        Helper.CreateSlot(slot);
        return Task.CompletedTask;
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
    private async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }
    [VeloxCommand]
    private Task Delete(object? parameter, CancellationToken ct)
    {
        Helper.Delete();
        return Task.CompletedTask;
    }

    public Task InitializeAsync()
    {
        Helper.Initialize(this);
        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        await Helper.CloseAsync();
    }

    public IWorkflowNodeViewModelHelper GetHelper() => Helper;
    public void SetHelperAsync(IWorkflowNodeViewModelHelper helper)
    {
        Helper = helper;
        helper.Initialize(this);
    }

    public void InitializeWorkflow()
    {
        Helper.Initialize(this);
    }
}