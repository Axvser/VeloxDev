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
public class NodeHelper<T> : IWorkflowNodeViewModelHelper
    where T : class, IWorkflowNodeViewModel
{
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    public event EventHandler<IWorkflowSlotViewModel>? SlotAdded;
    public event EventHandler<IWorkflowSlotViewModel>? SlotRemoved;

    public virtual void Install(IWorkflowNodeViewModel node)
    {
        Component = node as T;
        commands = node.GetStandardCommands();
        node.Slots.CollectionChanged += OnSlotsChanged;
    }
    public virtual void Uninstall(IWorkflowNodeViewModel node)
    {
        Component = null;
        commands = [];
        node.Slots.CollectionChanged -= OnSlotsChanged;
    }
    public virtual void Closing() => commands.StandardClosing();
    public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
    public virtual void Closed() => commands.StandardClosed();
    public virtual void CreateSlot(IWorkflowSlotViewModel slot) => Component?.StandardCreateSlot(slot);

    public virtual async Task BroadcastAsync(
        object? parameter,
        CancellationToken ct)
    {
        if (Component is not null) await Component.StandardBroadcastAsync(parameter, ct);
    }
    public virtual async Task ReverseBroadcastAsync(
        object? parameter,
        CancellationToken ct)
    {
        if (Component is not null) await Component.StandardReverseBroadcastAsync(parameter, ct);
    }
    public virtual Task WorkAsync(
        object? parameter,
        CancellationToken ct)
        => Task.CompletedTask;
    public virtual Task<bool> ValidateBroadcastAsync(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver,
        object? parameter,
        CancellationToken ct)
        => Task.FromResult(true);

    public virtual void SetAnchor(Anchor anchor) => Component?.StandardSetAnchor(anchor);
    public virtual void SetSize(Size size) => Component?.StandardSetSize(size);
    public virtual void Move(Offset offset) => Component?.StandardMove(offset);

    public virtual void Delete() => Component?.StandardDelete();

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
    protected virtual void OnSlotAdded(IWorkflowSlotViewModel slot) => SlotAdded?.Invoke(Component, slot);
    protected virtual void OnSlotRemoved(IWorkflowSlotViewModel slot) => SlotRemoved?.Invoke(Component, slot);
}
