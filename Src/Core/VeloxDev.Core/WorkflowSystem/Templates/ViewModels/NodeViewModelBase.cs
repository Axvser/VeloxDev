using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Node component interface")]
public sealed partial class NodeViewModelBase : IWorkflowNodeViewModel, IWorkflowIdentifiable
{
    private IWorkflowNodeViewModelHelper helper = new NodeHelper();
    public IWorkflowNodeViewModelHelper Helper
    {
        get => helper;
        private set
        {
            if (ReferenceEquals(helper, value)) return;
            OnPropertyChanging(nameof(Helper));
            helper = value;
            OnPropertyChanged(nameof(Helper));
        }
    }

    public string RuntimeId { get; } = Guid.NewGuid().ToString("N");

    public NodeViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private IWorkflowTreeViewModel? parent = null;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];
    private object? workResult = null;

    public object? WorkResult
    {
        get => workResult;
        set
        {
            if (Equals(workResult, value)) return;
            OnPropertyChanging(nameof(WorkResult));
            workResult = value;
            OnPropertyChanged(nameof(WorkResult));
        }
    }

    [VeloxCommand]
    private Task Move(object? parameter, CancellationToken ct)
    {
        if (parameter is not Offset offset) return Task.CompletedTask;
        Helper.Move(offset);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SetAnchor(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.SetAnchor(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SetSize(object? parameter, CancellationToken ct)
    {
        if (parameter is not Size scale) return Task.CompletedTask;
        Helper.SetSize(scale);
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
    private Task Delete(object? parameter, CancellationToken ct)
    {
        Helper.Delete();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private async Task Work(object? parameter, CancellationToken ct)
    {
        if (parameter is IWorkContext ctx && ctx.Sender is not null && ctx.Receiver is not null)
        {
            WorkResult = await Helper.ReceiveAsync(ctx.Parameter, ctx.Sender, ctx.Receiver, ct);
        }
        else
        {
            await Helper.WorkAsync(parameter, ct);
            WorkResult = null;
        }
    }
    [VeloxCommand]
    private async Task Broadcast(object? parameter, CancellationToken ct)
    {
        await Helper.BroadcastAsync(parameter, ct);
    }
    [VeloxCommand]
    private async Task ReverseBroadcast(object? parameter, CancellationToken ct)
    {
        await Helper.ReverseBroadcastAsync(parameter, ct);
    }
    [VeloxCommand]
    private async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    public IWorkflowNodeViewModelHelper GetHelper() => Helper;
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    public void SetHelper(IWorkflowNodeViewModelHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
