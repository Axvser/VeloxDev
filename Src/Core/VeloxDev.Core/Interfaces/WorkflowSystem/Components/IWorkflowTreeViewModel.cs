using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow tree: the workspace holding all nodes, slots and links.</summary>
[AgentContext(AgentLanguages.Chinese, "工作流Tree组件接口，维护一个工作空间内所有Node、Slot和Link组件")]
[AgentContext(AgentLanguages.English, "Workflow Tree component interface, maintaining all Node, Slot, and Link components within a workspace")]
public interface IWorkflowTreeViewModel : IWorkflowViewModel
{
    /// <summary>The canvas layout: its size and offset.</summary>
    [AgentContext(AgentLanguages.Chinese, "画布布局上下文，记录画布尺寸与偏移信息")]
    [AgentContext(AgentLanguages.English, "Canvas layout context, recording canvas size and offset information")]
    public CanvasLayout Layout { get; set; }

    /// <summary>The provisional link shown while a connection is being built.</summary>
    [AgentContext(AgentLanguages.Chinese, "仅在建立连接的过程中可见")]
    [AgentContext(AgentLanguages.English, "Only visible during the connection establishment process")]
    public IWorkflowLinkViewModel VirtualLink { get; set; }

    /// <summary>All nodes in the workspace.</summary>
    [AgentContext(AgentLanguages.Chinese, "所有Node组件")]
    [AgentContext(AgentLanguages.English, "All Node components")]
    public ObservableCollection<IWorkflowNodeViewModel> Nodes { get; set; }

    /// <summary>All links in the workspace.</summary>
    [AgentContext(AgentLanguages.Chinese, "所有Link组件")]
    [AgentContext(AgentLanguages.English, "All Link components")]
    public ObservableCollection<IWorkflowLinkViewModel> Links { get; set; }

    /// <summary>Link lookup keyed by sender slot and receiver slot.</summary>
    [AgentContext(AgentLanguages.Chinese, "Slot组件之间的连接关系映射")]
    [AgentContext(AgentLanguages.English, "Mapping of connections between Slot components")]
    public Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> LinksMap { get; set; }

    /// <summary>Creates a node; the parameter is an <see cref="IWorkflowNodeViewModel"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "创建节点，参数为IWorkflowNodeViewModel")]
    [AgentContext(AgentLanguages.English, "Create node command, parameter is IWorkflowNodeViewModel")]
    [AgentCommandParameter(typeof(IWorkflowNodeViewModel))]
    public IVeloxCommand CreateNodeCommand { get; }

    /// <summary>Updates the pointer position; the parameter is an <see cref="Anchor"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "更新触点位置，参数为Anchor")]
    [AgentContext(AgentLanguages.English, "Update pointer position, parameter is Anchor")]
    [AgentCommandParameter(typeof(Anchor))]
    public IVeloxCommand SetPointerCommand { get; }

    /// <summary>Clears the provisional link.</summary>
    [AgentContext(AgentLanguages.Chinese, "重置虚拟连接，参数为Null")]
    [AgentContext(AgentLanguages.English, "Reset virtual link, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand ResetVirtualLinkCommand { get; }

    /// <summary>Starts building a connection; the parameter is the sender <see cref="IWorkflowSlotViewModel"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "发起连接构建，参数为IWorkflowSlotViewModel")]
    [AgentContext(AgentLanguages.English, "Initiate connection construction, parameter is IWorkflowSlotViewModel")]
    [AgentCommandParameter(typeof(IWorkflowSlotViewModel))]
    public IVeloxCommand SendConnectionCommand { get; }

    /// <summary>Completes a connection; the parameter is the receiver <see cref="IWorkflowSlotViewModel"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "接收连接构建，参数为IWorkflowSlotViewModel")]
    [AgentContext(AgentLanguages.English, "Receive connection construction, parameter is IWorkflowSlotViewModel")]
    [AgentCommandParameter(typeof(IWorkflowSlotViewModel))]
    public IVeloxCommand ReceiveConnectionCommand { get; }

    /// <summary>Submits an action; the parameter is an <see cref="IWorkflowActionPair"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "提交操作，参数为IWorkflowActionPair")]
    [AgentContext(AgentLanguages.English, "Submit action, parameter is IWorkflowActionPair")]
    [AgentCommandParameter(typeof(IWorkflowActionPair))]
    public IVeloxCommand SubmitCommand { get; }

    /// <summary>Redoes the last undone action.</summary>
    [AgentContext(AgentLanguages.Chinese, "重做操作，参数为Null")]
    [AgentContext(AgentLanguages.English, "Redo action, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand RedoCommand { get; }

    /// <summary>Undoes the last action.</summary>
    [AgentContext(AgentLanguages.Chinese, "撤销操作，参数为Null")]
    [AgentContext(AgentLanguages.English, "Undo action, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand UndoCommand { get; }

    /// <summary>Gets the helper that carries this tree's behaviour.</summary>
    public IWorkflowTreeViewModelHelper GetHelper();

    /// <summary>Sets the helper that carries this tree's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(IWorkflowTreeViewModelHelper helper);
}

/// <summary>The behaviour behind an <see cref="IWorkflowTreeViewModel"/>.</summary>
public interface IWorkflowTreeViewModelHelper : IWorkflowHelper
{
    /// <summary>Raised after a node is added.</summary>
    public event EventHandler<IWorkflowNodeViewModel>? NodeAdded;

    /// <summary>Raised after a node is removed.</summary>
    public event EventHandler<IWorkflowNodeViewModel>? NodeRemoved;

    /// <summary>Raised after a link is added.</summary>
    public event EventHandler<IWorkflowLinkViewModel>? LinkAdded;

    /// <summary>Raised after a link is removed.</summary>
    public event EventHandler<IWorkflowLinkViewModel>? LinkRemoved;

    /// <summary>Raised when an item enters the visible set.</summary>
    public event EventHandler<IWorkflowViewModel>? VisibleItemAdded;

    /// <summary>Raised when an item leaves the visible set.</summary>
    public event EventHandler<IWorkflowViewModel>? VisibleItemRemoved;

    /// <summary>The items currently inside the visible set.</summary>
    public ObservableCollection<IWorkflowViewModel> VisibleItems { get; set; }

    /// <summary>The current viewport.</summary>
    public Viewport Viewport { get; set; }

    /// <summary>Attaches this helper to <paramref name="tree"/>.</summary>
    public void Install(IWorkflowTreeViewModel tree);

    /// <summary>Detaches this helper from <paramref name="tree"/>.</summary>
    public void Uninstall(IWorkflowTreeViewModel tree);

    /// <summary>Adds <paramref name="node"/> to the tree.</summary>
    public void CreateNode(IWorkflowNodeViewModel node);

    /// <summary>Creates the link between <paramref name="sender"/> and <paramref name="receiver"/>.</summary>
    public IWorkflowLinkViewModel CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver);

    /// <summary>Moves the connection pointer to <paramref name="anchor"/>.</summary>
    public void SetPointer(Anchor anchor);

    /// <summary>Whether a link from <paramref name="sender"/> to <paramref name="receiver"/> is allowed.</summary>
    public bool ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver);

    /// <summary>Starts a connection from <paramref name="slot"/>.</summary>
    public void SendConnection(IWorkflowSlotViewModel slot);

    /// <summary>Completes a connection at <paramref name="slot"/>.</summary>
    public void ReceiveConnection(IWorkflowSlotViewModel slot);

    /// <summary>Clears the provisional link.</summary>
    public void ResetVirtualLink();

    /// <summary>Rebuilds the visible set for <paramref name="viewport"/>.</summary>
    public void Virtualize(Viewport viewport);

    /// <summary>Submits <paramref name="actionPair"/> to the undo history.</summary>
    public void Submit(IWorkflowActionPair actionPair);

    /// <summary>Redoes the last undone action.</summary>
    public void Redo();

    /// <summary>Undoes the last action.</summary>
    public void Undo();

    /// <summary>Clears the undo/redo history.</summary>
    public void ClearHistory();

    /// <summary>Flags the tree as changed.</summary>
    public void MarkDirty();
}
