using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow node: the geometry, slots and dataflow commands of one node.</summary>
public interface INodeComponent : IWorkflowComponent
{
    /// <summary>The parent <see cref="ITreeComponent"/> this node lives in.</summary>
    public ITreeComponent? Parent { get; set; }

    /// <summary>The node's anchor position on the canvas.</summary>
    /// <remarks>
    /// The setter is responsible for raising <see cref="IWorkflowComponent.OnPropertyChanged"/> for
    /// <see cref="Anchor"/>: the framework assigns through this property and does not raise a second notification.
    /// </remarks>
    public Anchor Anchor { get; set; }

    /// <summary>The node's size.</summary>
    /// <remarks>
    /// The setter is responsible for raising <see cref="IWorkflowComponent.OnPropertyChanged"/> for
    /// <see cref="Size"/>: the framework assigns through this property and does not raise a second notification.
    /// </remarks>
    public Size Size { get; set; }

    /// <summary>The slots owned by this node.</summary>
    public ObservableCollection<ISlotComponent> Slots { get; set; }

    /// <summary>Moves the node by an <see cref="Offset"/>.</summary>
    [AgentCommandParameter(typeof(Offset))]
    public IVeloxCommand MoveCommand { get; }

    /// <summary>Sets the anchor position; the parameter is an <see cref="Anchor"/>.</summary>
    [AgentCommandParameter(typeof(Anchor))]
    public IVeloxCommand SetAnchorCommand { get; }

    /// <summary>Sets the node size; the parameter is a <see cref="Size"/>.</summary>
    [AgentCommandParameter(typeof(Size))]
    public IVeloxCommand SetSizeCommand { get; }

    /// <summary>Creates a slot; the parameter is an <see cref="ISlotComponent"/>.</summary>
    [AgentCommandParameter(typeof(ISlotComponent))]
    public IVeloxCommand CreateSlotCommand { get; }

    /// <summary>Deletes this node along with its slots and links.</summary>
    [AgentCommandParameter]
    public IVeloxCommand DeleteCommand { get; }

    /// <summary>Receives data and runs the node; the parameter is a nullable <see cref="ITaskContext"/>.</summary>
    [AgentCommandParameter(typeof(ITaskContext))]
    public IVeloxCommand ReceiveCommand { get; }

    /// <summary>Broadcasts data forward; the parameter is nullable.</summary>
    [AgentCommandParameter]
    public IVeloxCommand BroadcastCommand { get; }

    /// <summary>Broadcasts data in reverse; the parameter is nullable.</summary>
    [AgentCommandParameter]
    public IVeloxCommand ReverseBroadcastCommand { get; }

    /// <summary>Gets the helper that carries this node's behaviour.</summary>
    public INodeHelper GetHelper();

    /// <summary>Sets the helper that carries this node's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(INodeHelper helper);
}

/// <summary>The behaviour behind an <see cref="INodeComponent"/>.</summary>
public interface INodeHelper : IWorkflowHelper
{
    /// <summary>Raised after a slot is added to the node.</summary>
    public event EventHandler<ISlotComponent>? SlotAdded;

    /// <summary>Raised after a slot is removed from the node.</summary>
    public event EventHandler<ISlotComponent>? SlotRemoved;

    /// <summary>Attaches this helper to <paramref name="node"/>.</summary>
    public void Install(INodeComponent node);

    /// <summary>Detaches this helper from <paramref name="node"/>.</summary>
    public void Uninstall(INodeComponent node);

    /// <summary>Adds <paramref name="slot"/> to the node.</summary>
    public void CreateSlot(ISlotComponent slot);

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
