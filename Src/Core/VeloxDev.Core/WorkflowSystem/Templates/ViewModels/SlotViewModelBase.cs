using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Slot组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Slot component interface")]
[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public partial class SlotViewModelBase : IWorkflowSlotViewModel
{
    private IWorkflowSlotViewModelHelper Helper = new SlotHelper();

    public SlotViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> targets = [];
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> sources = [];
    [VeloxProperty] private IWorkflowNodeViewModel? parent = null;
    [VeloxProperty] private SlotChannel channel = SlotChannel.OneBoth;
    [VeloxProperty] private SlotState state = SlotState.StandBy;
    [VeloxProperty] private Anchor anchor = new();

    [VeloxCommand]
    protected virtual Task SetChannel(object? parameter, CancellationToken ct)
    {
        if (parameter is not SlotChannel slotChannel) return Task.CompletedTask;
        Helper.SetChannel(slotChannel);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task SendConnection(object? parameter, CancellationToken ct)
    {
        Helper.SendConnection();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task ReceiveConnection(object? parameter, CancellationToken ct)
    {
        Helper.ReceiveConnection();
        return Task.CompletedTask;
    }
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

    public virtual IWorkflowSlotViewModelHelper GetHelper() => Helper;
    public virtual void InitializeWorkflow() => Helper.Install(this);
    public virtual void SetHelper(IWorkflowSlotViewModelHelper helper)
    {
        Helper.Uninstall(this);
        helper.Install(this);
        Helper = helper;
    }
}
