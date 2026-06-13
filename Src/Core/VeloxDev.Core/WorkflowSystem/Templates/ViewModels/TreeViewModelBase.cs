using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Tree组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Tree component interface")]
[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public partial class TreeViewModelBase : IWorkflowTreeViewModel, IWorkflowIdentifiable
{
    private IWorkflowTreeViewModelHelper helper = new TreeHelper();
    public IWorkflowTreeViewModelHelper Helper
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

    public TreeViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private CanvasLayout layout = new();
    [VeloxProperty] private IWorkflowLinkViewModel virtualLink = new LinkViewModelBase() { Sender = new SlotViewModelBase(), Receiver = new SlotViewModelBase() };
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> nodes = [];
    [VeloxProperty] private ObservableCollection<IWorkflowLinkViewModel> links = [];
    [VeloxProperty] private Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> linksMap = [];

    [VeloxCommand]
    private Task CreateNode(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowNodeViewModel node) return Task.CompletedTask;
        Helper.CreateNode(node);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SetPointer(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.SetPointer(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task ResetVirtualLink(object? parameter, CancellationToken ct)
    {
        Helper.ResetVirtualLink();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SendConnection(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
        Helper.SendConnection(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task ReceiveConnection(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
        Helper.ReceiveConnection(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Submit(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowActionPair actionPair) return Task.CompletedTask;
        Helper.Submit(actionPair);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Redo(object? parameter, CancellationToken ct)
    {
        Helper.Redo();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Undo(object? parameter, CancellationToken ct)
    {
        Helper.Undo();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    public IWorkflowTreeViewModelHelper GetHelper() => Helper;
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    public void SetHelper(IWorkflowTreeViewModelHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
