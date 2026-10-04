using System.Collections.Specialized;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// [ Component Helper ] Provide standard supports for Node Component
/// </summary>
public class NodeHelper : NodeHelper<IWorkflowNodeViewModel>
{

}

/// <summary>
/// [ Component Helper ] Provide standard supports for Node Component
/// </summary>
/// <typeparam name="T"> The type of the Node ViewModel that this helper is designed for. </typeparam>
public class NodeHelper<T> : IWorkflowNodeViewModelHelper, IWorkflowNodeEvents, IWorkflowInputEvents
    where T : class, IWorkflowNodeViewModel
{
    /// <summary>The node this helper is installed on, when it matches <typeparamref name="T"/>.</summary>
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    /// <inheritdoc />
    public WorkflowInputRelay Input { get; } = new();

    /// <inheritdoc />
    public event EventHandler<IWorkflowSlotViewModel>? SlotAdded;
    /// <inheritdoc />
    public event EventHandler<IWorkflowSlotViewModel>? SlotRemoved;

    /// <inheritdoc />
    public virtual void Install(IWorkflowNodeViewModel node)
    {
        Component = node as T;
        commands = node.GetStandardCommands();
        node.Slots.CollectionChanged += OnSlotsChanged;
    }
    /// <inheritdoc />
    public virtual void Uninstall(IWorkflowNodeViewModel node)
    {
        Component = null;
        commands = [];
        node.Slots.CollectionChanged -= OnSlotsChanged;
    }
    /// <inheritdoc />
    public virtual void Closing() => commands.StandardClosing();
    /// <inheritdoc />
    public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
    /// <inheritdoc />
    public virtual void Closed() => commands.StandardClosed();
    /// <inheritdoc />
    public virtual void CreateSlot(IWorkflowSlotViewModel slot) => Component?.StandardCreateSlot(slot);

    /// <inheritdoc />
    public virtual async Task BroadcastAsync(
        object? parameter,
        CancellationToken ct)
    {
        if (Component is not null) await Component.StandardBroadcastAsync(parameter, ct);
    }
    /// <inheritdoc />
    public virtual async Task ReverseBroadcastAsync(
        object? parameter,
        CancellationToken ct)
    {
        if (Component is not null) await Component.StandardReverseBroadcastAsync(parameter, ct);
    }
    /// <inheritdoc />
    public virtual Task<object?> ReceiveAsync(
        ITaskContext context,
        CancellationToken ct)
        => Task.FromResult<object?>(null);
    /// <inheritdoc />
    public virtual Task<bool> AccessAsync(
        IAccessContext context,
        CancellationToken ct)
        => Task.FromResult(true);

    /// <inheritdoc />
    public virtual void SetAnchor(Anchor anchor)
    {
        Component?.StandardSetAnchor(anchor);
        Component?.Parent?.GetHelper().MarkDirty();
    }
    /// <inheritdoc />
    public virtual void SetSize(Size size)
    {
        Component?.StandardSetSize(size);
        Component?.Parent?.GetHelper().MarkDirty();
    }
    /// <inheritdoc />
    public virtual void Move(Offset offset)
    {
        Component?.StandardMove(offset);
        Component?.Parent?.GetHelper().MarkDirty();
    }

    /// <inheritdoc />
    public virtual void Delete() => Component?.StandardDelete();

    /// <summary>
    /// Raised before the node is placed somewhere new. Refusing here holds the node where it is — the per-event way
    /// to pin one node without disabling dragging for the whole surface.
    /// </summary>
    /// <remarks>The anchors on the argument are complete placements (layer included); see <see cref="NodeMoveEventArgs"/>.</remarks>
    public event EventHandler<NodeMoveEventArgs>? Moving;

    /// <summary>Raised after the node has been placed, carrying the same handle as <see cref="Moving"/>.</summary>
    public event EventHandler<NodeMoveEventArgs>? Moved;

    /// <summary>Raised before the node's size changes.</summary>
    public event EventHandler<NodeResizeEventArgs>? Resizing;

    /// <summary>Raised after the node's size changed, carrying the same handle as <see cref="Resizing"/>.</summary>
    public event EventHandler<NodeResizeEventArgs>? Resized;

    /// <summary>Raised before the node is torn down. Refusing here keeps it in the tree.</summary>
    public event EventHandler<NodeEventArgs>? Deleting;

    /// <summary>Raised after the node was torn down, carrying the same handle as <see cref="Deleting"/>.</summary>
    public event EventHandler<NodeEventArgs>? Deleted;

    /// <summary>
    /// Asks the host whether a placement may happen, and hands back the handle the caller must pass on to
    /// <see cref="RaiseMoved"/>. Called by the framework after it has computed both ends, before it touches the
    /// model.
    /// </summary>
    /// <param name="from">Where the node is now, layer included.</param>
    /// <param name="to">Where it is going, layer included.</param>
    public virtual WorkflowEventHandle RaiseMoving(Anchor from, Anchor to)
    {
        var handle = new WorkflowEventHandle();
        if (Component is { } node) Moving?.Invoke(node, new NodeMoveEventArgs(node, from, to, handle));
        return handle;
    }

    /// <summary>Reports a placement that happened, unless the handle silenced it.</summary>
    /// <param name="from">Where the node was, layer included.</param>
    /// <param name="to">Where it is now, layer included.</param>
    /// <param name="handle">The handle <see cref="RaiseMoving"/> returned.</param>
    public virtual void RaiseMoved(Anchor from, Anchor to, WorkflowEventHandle handle)
    {
        if (handle.StopPropagation || Component is not { } node) return;
        Moved?.Invoke(node, new NodeMoveEventArgs(node, from, to, handle));
    }

    /// <summary>Asks the host whether a resize may happen; see <see cref="RaiseMoving"/>.</summary>
    /// <param name="from">The current size.</param>
    /// <param name="to">The size it is going to.</param>
    public virtual WorkflowEventHandle RaiseResizing(Size from, Size to)
    {
        var handle = new WorkflowEventHandle();
        if (Component is { } node) Resizing?.Invoke(node, new NodeResizeEventArgs(node, from, to, handle));
        return handle;
    }

    /// <summary>Reports a resize that happened, unless the handle silenced it.</summary>
    /// <param name="from">The size it was.</param>
    /// <param name="to">The size it is now.</param>
    /// <param name="handle">The handle <see cref="RaiseResizing"/> returned.</param>
    public virtual void RaiseResized(Size from, Size to, WorkflowEventHandle handle)
    {
        if (handle.StopPropagation || Component is not { } node) return;
        Resized?.Invoke(node, new NodeResizeEventArgs(node, from, to, handle));
    }

    /// <summary>Asks the host whether the node may be torn down; see <see cref="RaiseMoving"/>.</summary>
    public virtual WorkflowEventHandle RaiseDeleting()
    {
        var handle = new WorkflowEventHandle();
        if (Component is { } node) Deleting?.Invoke(node, new NodeEventArgs(node, handle));
        return handle;
    }

    /// <summary>Reports a deletion that happened, unless the handle silenced it.</summary>
    /// <param name="handle">The handle <see cref="RaiseDeleting"/> returned.</param>
    public virtual void RaiseDeleted(WorkflowEventHandle handle)
    {
        if (handle.StopPropagation || Component is not { } node) return;
        Deleted?.Invoke(node, new NodeEventArgs(node, handle));
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) return;
                foreach (var item in e.NewItems)
                {
                    if (item is IWorkflowSlotViewModel slot)
                    {
                        OnSlotAdded(slot);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) return;
                foreach (var item in e.OldItems)
                {
                    if (item is IWorkflowSlotViewModel slot)
                    {
                        OnSlotRemoved(slot);
                    }
                }
                break;
        }
    }
    /// <summary>Raises <see cref="SlotAdded"/>.</summary>
    protected virtual void OnSlotAdded(IWorkflowSlotViewModel slot) => SlotAdded?.Invoke(Component, slot);
    /// <summary>Raises <see cref="SlotRemoved"/>.</summary>
    protected virtual void OnSlotRemoved(IWorkflowSlotViewModel slot) => SlotRemoved?.Invoke(Component, slot);
}
