using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

public partial class SlotViewModel : IWorkflowSlotViewModel
{
    private IWorkflowSlotViewModelHelper Helper = new WorkflowHelper.ViewModel.Slot();

    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> targets = [];
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> sources = [];
    [VeloxProperty] private IWorkflowNodeViewModel? parent;
    [VeloxProperty] private SlotChannel channel = SlotChannel.Default;
    [VeloxProperty] private SlotState state = SlotState.StandBy;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Anchor offset = new();
    [VeloxProperty] private Size size = new();

    [VeloxCommand]
    private async Task Press(object? parameter, CancellationToken ct)
    {
        await Helper.PressAsync();
    }
    [VeloxCommand]
    private async Task Move(object? parameter, CancellationToken ct)
    {
        await Helper.MoveAsync(parameter);
    }
    [VeloxCommand]
    private async Task Release(object? parameter, CancellationToken ct)
    {
        await Helper.ReleaseAsync();
    }
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

    public IWorkflowSlotViewModelHelper GetHelper() => Helper;
    public async Task SetHelperAsync(IWorkflowSlotViewModelHelper helper)
    {
        Helper = helper;
        await helper.InitializeAsync(this);
    }
}