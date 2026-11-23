using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow tree: the workspace holding all nodes, slots and links.</summary>
public interface ITreeComponent : IWorkflowComponent
{
    /// <summary>The canvas layout: its size and offset.</summary>
    public CanvasLayout Layout { get; set; }

    /// <summary>The provisional link shown while a connection is being built.</summary>
    public ILinkComponent VirtualLink { get; set; }

    /// <summary>All nodes in the workspace.</summary>
    public ObservableCollection<INodeComponent> Nodes { get; set; }

    /// <summary>All links in the workspace.</summary>
    public ObservableCollection<ILinkComponent> Links { get; set; }

    /// <summary>Link lookup keyed by sender slot and receiver slot.</summary>
    public Dictionary<ISlotComponent, Dictionary<ISlotComponent, ILinkComponent>> LinksMap { get; set; }

    /// <summary>Creates a node; the parameter is an <see cref="INodeComponent"/>.</summary>
    [AgentCommandParameter(typeof(INodeComponent))]
    public IVeloxCommand CreateNodeCommand { get; }

    /// <summary>Updates the pointer position; the parameter is an <see cref="Anchor"/>.</summary>
    [AgentCommandParameter(typeof(Anchor))]
    public IVeloxCommand SetPointerCommand { get; }

    /// <summary>Clears the provisional link.</summary>
    [AgentCommandParameter]
    public IVeloxCommand ResetVirtualLinkCommand { get; }

    /// <summary>Starts building a connection; the parameter is the sender <see cref="ISlotComponent"/>.</summary>
    [AgentCommandParameter(typeof(ISlotComponent))]
    public IVeloxCommand SendConnectionCommand { get; }

    /// <summary>Completes a connection; the parameter is the receiver <see cref="ISlotComponent"/>.</summary>
    [AgentCommandParameter(typeof(ISlotComponent))]
    public IVeloxCommand ReceiveConnectionCommand { get; }

    /// <summary>Submits an action; the parameter is an <see cref="IActionPair"/>.</summary>
    [AgentCommandParameter(typeof(IActionPair))]
    public IVeloxCommand SubmitCommand { get; }

    /// <summary>Redoes the last undone action.</summary>
    [AgentCommandParameter]
    public IVeloxCommand RedoCommand { get; }

    /// <summary>Undoes the last action.</summary>
    [AgentCommandParameter]
    public IVeloxCommand UndoCommand { get; }

    /// <summary>Gets the helper that carries this tree's behaviour.</summary>
    public ITreeHelper GetHelper();

    /// <summary>Sets the helper that carries this tree's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(ITreeHelper helper);
}

/// <summary>The behaviour behind an <see cref="ITreeComponent"/>.</summary>
public interface ITreeHelper : IWorkflowHelper
{
    /// <summary>Raised after a node is added.</summary>
    public event EventHandler<INodeComponent>? NodeAdded;

    /// <summary>Raised after a node is removed.</summary>
    public event EventHandler<INodeComponent>? NodeRemoved;

    /// <summary>Raised after a link is added.</summary>
    public event EventHandler<ILinkComponent>? LinkAdded;

    /// <summary>Raised after a link is removed.</summary>
    public event EventHandler<ILinkComponent>? LinkRemoved;

    /// <summary>Raised when an item enters the visible set.</summary>
    public event EventHandler<IWorkflowComponent>? VisibleItemAdded;

    /// <summary>Raised when an item leaves the visible set.</summary>
    public event EventHandler<IWorkflowComponent>? VisibleItemRemoved;

    /// <summary>The items currently inside the visible set.</summary>
    public ObservableCollection<IWorkflowComponent> VisibleItems { get; set; }

    /// <summary>The current viewport.</summary>
    public Viewport Viewport { get; set; }

    /// <summary>Attaches this helper to <paramref name="tree"/>.</summary>
    public void Install(ITreeComponent tree);

    /// <summary>Detaches this helper from <paramref name="tree"/>.</summary>
    public void Uninstall(ITreeComponent tree);

    /// <summary>Adds <paramref name="node"/> to the tree.</summary>
    public void CreateNode(INodeComponent node);

    /// <summary>Creates the link between <paramref name="sender"/> and <paramref name="receiver"/>.</summary>
    public ILinkComponent CreateLink(ISlotComponent sender, ISlotComponent receiver);

    /// <summary>Moves the connection pointer to <paramref name="anchor"/>.</summary>
    public void SetPointer(Anchor anchor);

    /// <summary>Whether a link from <paramref name="sender"/> to <paramref name="receiver"/> is allowed.</summary>
    public bool ValidateConnection(ISlotComponent sender, ISlotComponent receiver);

    /// <summary>Starts a connection from <paramref name="slot"/>.</summary>
    public void SendConnection(ISlotComponent slot);

    /// <summary>Completes a connection at <paramref name="slot"/>.</summary>
    public void ReceiveConnection(ISlotComponent slot);

    /// <summary>Clears the provisional link.</summary>
    public void ResetVirtualLink();

    /// <summary>Rebuilds the visible set for <paramref name="viewport"/>.</summary>
    public void Virtualize(Viewport viewport);

    /// <summary>Submits <paramref name="actionPair"/> to the undo history.</summary>
    public void Submit(IActionPair actionPair);

    /// <summary>Redoes the last undone action.</summary>
    public void Redo();

    /// <summary>Undoes the last action.</summary>
    public void Undo();

    /// <summary>Clears the undo/redo history.</summary>
    public void ClearHistory();

    /// <summary>Flags the tree as changed.</summary>
    public void MarkDirty();
}
