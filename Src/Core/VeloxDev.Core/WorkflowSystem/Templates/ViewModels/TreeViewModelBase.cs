using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Tree组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Tree component interface")]
[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public partial class TreeViewModelBase : IWorkflowTreeViewModel
{
    private IWorkflowTreeViewModelHelper Helper = new TreeHelper();

    public TreeViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private IWorkflowLinkViewModel virtualLink = new LinkViewModelBase() { Sender = new SlotViewModelBase(), Receiver = new SlotViewModelBase() };
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> nodes = [];
    [VeloxProperty] private ObservableCollection<IWorkflowLinkViewModel> links = [];
    [VeloxProperty] private Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> linksMap = [];

    [VeloxCommand]
    protected virtual Task CreateNode(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowNodeViewModel node) return Task.CompletedTask;
        Helper.CreateNode(node);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task SetPointer(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.SetPointer(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task ResetVirtualLink(object? parameter, CancellationToken ct)
    {
        Helper.ResetVirtualLink();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task SendConnection(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
        Helper.SendConnection(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task ReceiveConnection(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
        Helper.ReceiveConnection(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task Submit(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowActionPair actionPair) return Task.CompletedTask;
        Helper.Submit(actionPair);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task Redo(object? parameter, CancellationToken ct)
    {
        Helper.Redo();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual Task Undo(object? parameter, CancellationToken ct)
    {
        Helper.Undo();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    public virtual IWorkflowTreeViewModelHelper GetHelper() => Helper;
    public virtual void InitializeWorkflow() => Helper.Install(this);
    public virtual void SetHelper(IWorkflowTreeViewModelHelper helper)
    {
        Helper.Uninstall(this);
        helper.Install(this);
        Helper = helper;
    }
}
