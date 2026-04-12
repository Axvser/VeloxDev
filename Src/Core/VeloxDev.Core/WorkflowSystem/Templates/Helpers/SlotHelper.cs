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
public class SlotHelper<T> : IWorkflowSlotViewModelHelper
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
