using System.Collections.Specialized;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// [ Component Helper ] Provide standard supports for Slot Component.
/// </summary>
public class SlotHelper : SlotHelper<IWorkflowSlotViewModel>
{

}

/// <summary>
/// [ Component Helper ] Provide standard supports for Slot Component.
/// </summary>
/// <typeparam name="T">The type of the Slot ViewModel that this helper is designed for. </typeparam>
public class SlotHelper<T> : IWorkflowSlotViewModelHelper, IWorkflowSlotEvents, IInputEvents
    where T : class, IWorkflowSlotViewModel
{
    /// <summary>The slot this helper is installed on, when it matches <typeparamref name="T"/>.</summary>
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    /// <inheritdoc />
    public InputRelay Input { get; } = new();

    /// <inheritdoc />
    public event EventHandler<IWorkflowSlotViewModel>? TargetAdded;
    /// <inheritdoc />
    public event EventHandler<IWorkflowSlotViewModel>? TargetRemoved;
    /// <inheritdoc />
    public event EventHandler<IWorkflowSlotViewModel>? SourceAdded;
    /// <inheritdoc />
    public event EventHandler<IWorkflowSlotViewModel>? SourceRemoved;

    /// <inheritdoc />
    public virtual void Install(IWorkflowSlotViewModel slot)
    {
        Component = slot as T;
        commands = slot.GetStandardCommands();
        slot.Targets.CollectionChanged += OnTargetsChanged;
        slot.Sources.CollectionChanged += OnSourcesChanged;
    }
    /// <inheritdoc />
    public virtual void Uninstall(IWorkflowSlotViewModel slot)
    {
        Component = null;
        commands = [];
        slot.Targets.CollectionChanged -= OnTargetsChanged;
        slot.Sources.CollectionChanged -= OnSourcesChanged;
    }

    /// <inheritdoc />
    public virtual void Closing() => commands.StandardClosing();
    /// <inheritdoc />
    public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
    /// <inheritdoc />
    public virtual void Closed() => commands.StandardClosed();

    /// <inheritdoc />
    public virtual void SetChannel(SlotChannel channel) => Component?.StandardSetChannel(channel);

    /// <inheritdoc />
    public virtual void UpdateState() => Component?.StandardUpdateState();

    /// <inheritdoc />
    public virtual void SendConnection() => Component?.StandardApplyConnection();
    /// <inheritdoc />
    public virtual void ReceiveConnection() => Component?.StandardReceiveConnection();

    /// <inheritdoc />
    public virtual void Delete() => Component?.StandardDelete();

    /// <summary>
    /// Raised before the slot's channel changes. A channel decides how many connections the slot may hold and
    /// whether it takes senders, receivers or both, so changing it can tear connections down — refusing here is how
    /// a host protects one slot from that.
    /// </summary>
    public event EventHandler<SlotChannelEventArgs>? ChannelChanging;

    /// <summary>Raised after the channel changed, carrying the same handle as <see cref="ChannelChanging"/>.</summary>
    public event EventHandler<SlotChannelEventArgs>? ChannelChanged;

    /// <summary>
    /// Asks the host whether the channel may change, and hands back the handle the caller passes on to
    /// <see cref="RaiseChannelChanged"/>.
    /// </summary>
    /// <param name="from">The current channel.</param>
    /// <param name="to">The channel it is going to.</param>
    public virtual WorkflowEventHandle RaiseChannelChanging(SlotChannel from, SlotChannel to)
    {
        var handle = new WorkflowEventHandle();
        if (Component is { } slot) ChannelChanging?.Invoke(slot, new SlotChannelEventArgs(slot, from, to, handle));
        return handle;
    }

    /// <summary>Reports a channel change that happened, unless the handle silenced it.</summary>
    /// <param name="from">The channel it was.</param>
    /// <param name="to">The channel it is now.</param>
    /// <param name="handle">The handle <see cref="RaiseChannelChanging"/> returned.</param>
    public virtual void RaiseChannelChanged(SlotChannel from, SlotChannel to, WorkflowEventHandle handle)
    {
        if (handle.StopPropagation || Component is not { } slot) return;
        ChannelChanged?.Invoke(slot, new SlotChannelEventArgs(slot, from, to, handle));
    }

    private void OnTargetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) return;
                foreach (var item in e.NewItems)
                {
                    if (item is IWorkflowSlotViewModel slot)
                    {
                        OnTargetAdded(slot);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) return;
                foreach (var item in e.OldItems)
                {
                    if (item is IWorkflowSlotViewModel slot)
                    {
                        OnTargetRemoved(slot);
                    }
                }
                break;
        }
    }
    /// <summary>Raises <see cref="TargetAdded"/>.</summary>
    protected virtual void OnTargetAdded(IWorkflowSlotViewModel slot) => TargetAdded?.Invoke(Component, slot);
    /// <summary>Raises <see cref="TargetRemoved"/>.</summary>
    protected virtual void OnTargetRemoved(IWorkflowSlotViewModel slot) => TargetRemoved?.Invoke(Component, slot);

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) return;
                foreach (var item in e.NewItems)
                {
                    if (item is IWorkflowSlotViewModel slot)
                    {
                        OnSourceAdded(slot);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) return;
                foreach (var item in e.OldItems)
                {
                    if (item is IWorkflowSlotViewModel slot)
                    {
                        OnSourceRemoved(slot);
                    }
                }
                break;
        }
    }
    /// <summary>Raises <see cref="SourceAdded"/>.</summary>
    protected virtual void OnSourceAdded(IWorkflowSlotViewModel slot) => SourceAdded?.Invoke(Component, slot);
    /// <summary>Raises <see cref="SourceRemoved"/>.</summary>
    protected virtual void OnSourceRemoved(IWorkflowSlotViewModel slot) => SourceRemoved?.Invoke(Component, slot);
}
