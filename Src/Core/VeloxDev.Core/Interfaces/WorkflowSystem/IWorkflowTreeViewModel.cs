using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Tree组件接口，维护一个工作空间内所有Node、Slot和Link组件")]
[AgentContext(AgentLanguages.English, "Workflow Tree component interface, maintaining all Node, Slot, and Link components within a workspace")]
public interface IWorkflowTreeViewModel : IWorkflowViewModel
{
    [AgentContext(AgentLanguages.Chinese, "仅在建立连接的过程中可见")]
    [AgentContext(AgentLanguages.English, "Only visible during the connection establishment process")]
    public IWorkflowLinkViewModel VirtualLink { get; set; }

    [AgentContext(AgentLanguages.Chinese, "所有Node组件")]
    [AgentContext(AgentLanguages.English, "All Node components")]
    public ObservableCollection<IWorkflowNodeViewModel> Nodes { get; set; }

    [AgentContext(AgentLanguages.Chinese, "所有Link组件")]
    [AgentContext(AgentLanguages.English, "All Link components")]
    public ObservableCollection<IWorkflowLinkViewModel> Links { get; set; }

    [AgentContext(AgentLanguages.Chinese, "Slot组件之间的连接关系映射")]
    [AgentContext(AgentLanguages.English, "Mapping of connections between Slot components")]
    public Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> LinksMap { get; set; }

    [AgentContext(AgentLanguages.Chinese, "创建节点，参数为IWorkflowNodeViewModel")]
    [AgentContext(AgentLanguages.English, "Create node command, parameter is IWorkflowNodeViewModel")]
    [AgentCommandParameter(typeof(IWorkflowNodeViewModel))]
    public IVeloxCommand CreateNodeCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "更新触点位置，参数为Anchor")]
    [AgentContext(AgentLanguages.English, "Update pointer position, parameter is Anchor")]
    [AgentCommandParameter(typeof(Anchor))]
    public IVeloxCommand SetPointerCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "重置虚拟连接，参数为Null")]
    [AgentContext(AgentLanguages.English, "Reset virtual link, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand ResetVirtualLinkCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "发起连接构建，参数为IWorkflowSlotViewModel")]
    [AgentContext(AgentLanguages.English, "Initiate connection construction, parameter is IWorkflowSlotViewModel")]
    [AgentCommandParameter(typeof(IWorkflowSlotViewModel))]
    public IVeloxCommand SendConnectionCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "接收连接构建，参数为IWorkflowSlotViewModel")]
    [AgentContext(AgentLanguages.English, "Receive connection construction, parameter is IWorkflowSlotViewModel")]
    [AgentCommandParameter(typeof(IWorkflowSlotViewModel))]
    public IVeloxCommand ReceiveConnectionCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "提交操作，参数为IWorkflowActionPair")]
    [AgentContext(AgentLanguages.English, "Submit action, parameter is IWorkflowActionPair")]
    [AgentCommandParameter(typeof(IWorkflowActionPair))]
    public IVeloxCommand SubmitCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "重做操作，参数为Null")]
    [AgentContext(AgentLanguages.English, "Redo action, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand RedoCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "撤销操作，参数为Null")]
    [AgentContext(AgentLanguages.English, "Undo action, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand UndoCommand { get; }

    public IWorkflowTreeViewModelHelper GetHelper();
    public void SetHelper(IWorkflowTreeViewModelHelper helper);
}

public interface IWorkflowTreeViewModelHelper : IWorkflowHelper
{
    public event EventHandler<IWorkflowNodeViewModel>? NodeAdded;
    public event EventHandler<IWorkflowNodeViewModel>? NodeRemoved;
    public event EventHandler<IWorkflowLinkViewModel>? LinkAdded;
    public event EventHandler<IWorkflowLinkViewModel>? LinkRemoved;

    public void Install(IWorkflowTreeViewModel tree);
    public void Uninstall(IWorkflowTreeViewModel tree);

    public void CreateNode(IWorkflowNodeViewModel node);
    public IWorkflowLinkViewModel CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver);

    public void SetPointer(Anchor anchor);
    public bool ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver);
    public void SendConnection(IWorkflowSlotViewModel slot);
    public void ReceiveConnection(IWorkflowSlotViewModel slot);
    public void ResetVirtualLink();

    public void Submit(IWorkflowActionPair actionPair);
    public void Redo();
    public void Undo();
    public void ClearHistory();
}
