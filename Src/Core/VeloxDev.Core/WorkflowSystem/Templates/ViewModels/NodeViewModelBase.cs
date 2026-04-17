using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Node component interface")]
[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public partial class NodeViewModelBase : IWorkflowNodeViewModel, IWorkflowIdentifiable
{
    private IWorkflowNodeViewModelHelper Helper = new NodeHelper();
    public string RuntimeId { get; } = Guid.NewGuid().ToString("N");

    public NodeViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private IWorkflowTreeViewModel? parent = null;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];

    [VeloxCommand]
    protected virtual Task Move(object? parameter, CancellationToken ct)
    {
        if (parameter is not Offset offset) return Task.CompletedTask;
        Helper.Move(offset);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task SetAnchor(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.SetAnchor(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task SetSize(object? parameter, CancellationToken ct)
    {
        if (parameter is not Size scale) return Task.CompletedTask;
        Helper.SetSize(scale);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task CreateSlot(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
        Helper.CreateSlot(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task Delete(object? parameter, CancellationToken ct)
    {
        Helper.Delete();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual async Task Work(object? parameter, CancellationToken ct)
    {
        await Helper.WorkAsync(parameter, ct);
    }
    [VeloxCommand]
    protected virtual async Task Broadcast(object? parameter, CancellationToken ct)
    {
        await Helper.BroadcastAsync(parameter, ct);
    }
    [VeloxCommand]
    protected virtual async Task ReverseBroadcast(object? parameter, CancellationToken ct)
    {
        await Helper.ReverseBroadcastAsync(parameter, ct);
    }
    [VeloxCommand]
    protected virtual async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    public virtual IWorkflowNodeViewModelHelper GetHelper() => Helper;
    public virtual void InitializeWorkflow() => Helper.Install(this);
    public virtual void SetHelper(IWorkflowNodeViewModelHelper helper)
    {
        Helper.Uninstall(this);
        helper.Install(this);
        Helper = helper;
    }
}
