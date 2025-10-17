using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

public partial class TreeViewModel : IWorkflowTreeViewModel
{
    private IWorkflowTreeViewModelHelper Helper = new WorkflowHelper.ViewModel.Tree();

    [VeloxProperty] private IWorkflowLinkViewModel virtualLink = new LinkViewModel();
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> nodes = [];
    [VeloxProperty] private ObservableCollection<IWorkflowLinkGroupViewModel> linkGroups = [];

    [VeloxCommand]
    private async Task SubmitActionPair(object? parameter, CancellationToken ct)
    {
        await Helper.SubmitActionPairAsync(parameter);
    }
    [VeloxCommand]
    private async Task CreateNode(object? parameter, CancellationToken ct)
    {
        await Helper.CreateNodeAsync(parameter);
    }
    [VeloxCommand]
    private async Task PressSlot(object? parameter, CancellationToken ct)
    {
        await Helper.PressSlotAsync(parameter);
    }
    [VeloxCommand]
    private async Task MovePointer(object? parameter, CancellationToken ct)
    {
        await Helper.MovePointerAsync(parameter);
    }
    [VeloxCommand]
    private async Task ReleaseSlot(object? parameter, CancellationToken ct)
    {
        await Helper.ReleaseSlotAsync(parameter);
    }
    [VeloxCommand]
    private async Task Redo(object? parameter, CancellationToken ct)
    {
        await Helper.RedoAsync();
    }
    [VeloxCommand]
    private async Task Undo(object? parameter, CancellationToken ct)
    {
        await Helper.UndoAsync();
    }

    public async Task InitializeAsync()
    {
        await Helper.InitializeAsync(this);
    }

    public async Task CloseAsync()
    {
        await Helper.CloseAsync();
    }

    public IWorkflowTreeViewModelHelper GetHelper() => Helper;
    public async Task SetHelperAsync(IWorkflowTreeViewModelHelper helper)
    {
        Helper = helper;
        await helper.InitializeAsync(this);
    }
}