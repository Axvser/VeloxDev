using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow node: the geometry, slots and dataflow commands of one node.</summary>
[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口，维护节点的空间信息、Slot集合以及广播行为")]
[AgentContext(AgentLanguages.English, "Workflow Node component interface, maintaining node geometry, slot collection, and broadcast behaviors")]
public interface IWorkflowNodeViewModel : IWorkflowViewModel
{
    /// <summary>The parent <see cref="IWorkflowTreeViewModel"/> this node lives in.</summary>
    [AgentContext(AgentLanguages.Chinese, "所属的Tree组件")]
    [AgentContext(AgentLanguages.English, "The parent Tree component")]
    public IWorkflowTreeViewModel? Parent { get; set; }

    /// <summary>The node's anchor position on the canvas.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Node在画布中的锚点坐标")]
    [AgentContext(AgentLanguages.English, "The anchor position of the current node on the canvas")]
    public Anchor Anchor { get; set; }

    /// <summary>The node's size.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Node的尺寸")]
    [AgentContext(AgentLanguages.English, "The size of the current node")]
    public Size Size { get; set; }

    /// <summary>The slots owned by this node.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Node包含的所有Slot组件")]
    [AgentContext(AgentLanguages.English, "All Slot components owned by the current node")]
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; }

    /// <summary>Moves the node by an <see cref="Offset"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "移动节点，参数为Offset")]
    [AgentContext(AgentLanguages.English, "Move node command, parameter is Offset")]
    [AgentCommandParameter(typeof(Offset))]
    public IVeloxCommand MoveCommand { get; }

    /// <summary>Sets the anchor position; the parameter is an <see cref="Anchor"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "设置锚点坐标，参数为Anchor")]
    [AgentContext(AgentLanguages.English, "Set anchor command, parameter is Anchor")]
    [AgentCommandParameter(typeof(Anchor))]
    public IVeloxCommand SetAnchorCommand { get; }

    /// <summary>Sets the node size; the parameter is a <see cref="Size"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "设置节点尺寸，参数为Size")]
    [AgentContext(AgentLanguages.English, "Set size command, parameter is Size")]
    [AgentCommandParameter(typeof(Size))]
    public IVeloxCommand SetSizeCommand { get; }

    /// <summary>Creates a slot; the parameter is an <see cref="IWorkflowSlotViewModel"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "创建Slot，参数为IWorkflowSlotViewModel")]
    [AgentContext(AgentLanguages.English, "Create slot command, parameter is IWorkflowSlotViewModel")]
    [AgentCommandParameter(typeof(IWorkflowSlotViewModel))]
    public IVeloxCommand CreateSlotCommand { get; }

    /// <summary>Deletes this node along with its slots and links.</summary>
    [AgentContext(AgentLanguages.Chinese, "删除当前Node，参数为Null，相关Slot和Link会被删除")]
    [AgentContext(AgentLanguages.English, "Delete the current node, parameter is Null, related Slots and Links will be deleted")]
    [AgentCommandParameter]
    public IVeloxCommand DeleteCommand { get; }

    /// <summary>Receives data and runs the node; the parameter is a nullable <see cref="ITaskContext"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "接收数据并执行节点，参数为可空的 ITaskContext（含 data/sender/receiver）")]
    [AgentContext(AgentLanguages.English, "Receive data and execute the node; parameter is a nullable ITaskContext (data/sender/receiver)")]
    [AgentCommandParameter(typeof(ITaskContext))]
    public IVeloxCommand ReceiveCommand { get; }

    /// <summary>Broadcasts data forward; the parameter is nullable.</summary>
    [AgentContext(AgentLanguages.Chinese, "正向广播数据，参数为Nullable")]
    [AgentContext(AgentLanguages.English, "Broadcast data forward, parameter is Nullable")]
    [AgentCommandParameter]
    public IVeloxCommand BroadcastCommand { get; }

    /// <summary>Broadcasts data in reverse; the parameter is nullable.</summary>
    [AgentContext(AgentLanguages.Chinese, "反向广播数据，参数为Nullable")]
    [AgentContext(AgentLanguages.English, "Broadcast data in reverse direction, parameter is Nullable")]
    [AgentCommandParameter]
    public IVeloxCommand ReverseBroadcastCommand { get; }

    /// <summary>Gets the helper that carries this node's behaviour.</summary>
    public IWorkflowNodeViewModelHelper GetHelper();

    /// <summary>Sets the helper that carries this node's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(IWorkflowNodeViewModelHelper helper);
}

/// <summary>The behaviour behind an <see cref="IWorkflowNodeViewModel"/>.</summary>
public interface IWorkflowNodeViewModelHelper : IWorkflowHelper
{
    /// <summary>Raised after a slot is added to the node.</summary>
    public event EventHandler<IWorkflowSlotViewModel>? SlotAdded;

    /// <summary>Raised after a slot is removed from the node.</summary>
    public event EventHandler<IWorkflowSlotViewModel>? SlotRemoved;

    /// <summary>Attaches this helper to <paramref name="node"/>.</summary>
    public void Install(IWorkflowNodeViewModel node);

    /// <summary>Detaches this helper from <paramref name="node"/>.</summary>
    public void Uninstall(IWorkflowNodeViewModel node);

    /// <summary>Adds <paramref name="slot"/> to the node.</summary>
    public void CreateSlot(IWorkflowSlotViewModel slot);

    /// <summary>Moves the node by <paramref name="offset"/>.</summary>
    public void Move(Offset offset);

    /// <summary>Sets the node's anchor to <paramref name="newValue"/>.</summary>
    public void SetAnchor(Anchor newValue);

    /// <summary>Sets the node's size to <paramref name="newValue"/>.</summary>
    public void SetSize(Size newValue);

    /// <summary>
    /// Called when this node receives a dataflow task — the single execution entry.
    /// The source generator's Receive handler forwards the incoming
    /// <see cref="ITaskContext"/> here. Returning a non-null value allows the
    /// Compiler to chain results.
    /// </summary>
    /// <param name="context">The task context (data/sender/receiver, all nullable).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct);

    /// <summary>Broadcasts <paramref name="parameter"/> forward.</summary>
    public Task BroadcastAsync(object? parameter, CancellationToken ct);

    /// <summary>Broadcasts <paramref name="parameter"/> in reverse.</summary>
    public Task ReverseBroadcastAsync(object? parameter, CancellationToken ct);

    /// <summary>
    /// Dataflow access validation: the same hook is reused across both phases, taking a dataflow access context
    /// <see cref="IAccessContext"/> (carrying the two end slots Sender/Receiver and the payload Data).
    /// - Compile phase: <see cref="IAccessContext.IsCompilePhase"/> is true and Data is null, doing static detection
    ///   without data;
    /// - Runtime phase: <see cref="IAccessContext.IsCompilePhase"/> is false and Data carries the payload, doing
    ///   real-time detection.
    /// Returning false skips that connection as "unconnected" (runtime broadcast does not deliver; the compile
    /// phase does not include it in the compiled graph).
    /// </summary>
    public Task<bool> AccessAsync(IAccessContext context, CancellationToken ct);

    /// <summary>Deletes the node this helper is installed on.</summary>
    public void Delete();
}
