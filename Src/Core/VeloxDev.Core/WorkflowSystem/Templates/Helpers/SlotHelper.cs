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
public class SlotHelper<T> : IWorkflowSlotViewModelHelper, IWorkflowSlotEvents
    where T : class, IWorkflowSlotViewModel
{
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    public event EventHandler<IWorkflowSlotViewModel>? TargetAdded;
    public event EventHandler<IWorkflowSlotViewModel>? TargetRemoved;
    public event EventHandler<IWorkflowSlotViewModel>? SourceAdded;
    public event EventHandler<IWorkflowSlotViewModel>? SourceRemoved;

    public virtual void Install(IWorkflowSlotViewModel slot)
    {
        Component = slot as T;
        commands = slot.GetStandardCommands();
        slot.Targets.CollectionChanged += OnTargetsChanged;
        slot.Sources.CollectionChanged += OnSourcesChanged;
    }
    public virtual void Uninstall(IWorkflowSlotViewModel slot)
    {
        Component = null;
        commands = [];
        slot.Targets.CollectionChanged -= OnTargetsChanged;
        slot.Sources.CollectionChanged -= OnSourcesChanged;
    }

    public virtual void Closing() => commands.StandardClosing();
    public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
    public virtual void Closed() => commands.StandardClosed();

    public virtual void SetChannel(SlotChannel channel) => Component?.StandardSetChannel(channel);

    public virtual void UpdateState() => Component?.StandardUpdateState();

    public virtual void SendConnection() => Component?.StandardApplyConnection();
    public virtual void ReceiveConnection() => Component?.StandardReceiveConnection();

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
    protected virtual void OnTargetAdded(IWorkflowSlotViewModel slot) => TargetAdded?.Invoke(Component, slot);
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
    protected virtual void OnSourceAdded(IWorkflowSlotViewModel slot) => SourceAdded?.Invoke(Component, slot);
    protected virtual void OnSourceRemoved(IWorkflowSlotViewModel slot) => SourceRemoved?.Invoke(Component, slot);
}
