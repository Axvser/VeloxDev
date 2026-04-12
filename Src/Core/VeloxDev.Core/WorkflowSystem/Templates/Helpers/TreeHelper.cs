using System.Collections.Specialized;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// [ Component Helper ] Provide standard supports for Tree Component
/// </summary>
public class TreeHelper : TreeHelper<IWorkflowTreeViewModel>
{

}

/// <summary>
/// [ Component Helper ] Provide standard supports for Tree Component
/// </summary>
/// <typeparam name="T">The type of the Tree ViewModel that this helper is designed for.</typeparam>
public class TreeHelper<T> : IWorkflowTreeViewModelHelper
    where T : class, IWorkflowTreeViewModel
{
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    public event EventHandler<IWorkflowNodeViewModel>? NodeAdded;
    public event EventHandler<IWorkflowNodeViewModel>? NodeRemoved;
    public event EventHandler<IWorkflowLinkViewModel>? LinkAdded;
    public event EventHandler<IWorkflowLinkViewModel>? LinkRemoved;

    public virtual void Install(IWorkflowTreeViewModel tree)
    {
        Component = tree as T;
        commands = tree.GetStandardCommands();
        tree.Nodes.CollectionChanged += OnNodesChanged;
        tree.Links.CollectionChanged += OnLinksChanged;
    }
    public virtual void Uninstall(IWorkflowTreeViewModel tree)
    {
        Component = null;
        commands = [];
        tree.Nodes.CollectionChanged -= OnNodesChanged;
        tree.Links.CollectionChanged -= OnLinksChanged;
    }

    public virtual void Closing() => commands.StandardClosing();
    public virtual async Task CloseAsync()
    {
        if (Component is not null) await Component.StandardCloseAsync();
    }
    public virtual void Closed() => commands.StandardClosed();

    public virtual IWorkflowLinkViewModel CreateLink(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver)
        => new LinkViewModelBase()
        {
            Sender = sender,
            Receiver = receiver,
        };

    public virtual void CreateNode(IWorkflowNodeViewModel node)
        => Component?.StandardCreateNode(node);

    public virtual void SetPointer(Anchor anchor)
        => Component?.StandardSetPointer(anchor);

    #region Data CallBack
    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) return;
                foreach (var item in e.NewItems)
                {
                    if (item is IWorkflowNodeViewModel node)
                    {
                        OnNodeAdded(node);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) return;
                foreach (var item in e.OldItems)
                {
                    if (item is IWorkflowNodeViewModel node)
                    {
                        OnNodeRemoved(node);
                    }
                }
                break;
        }
    }
    protected virtual void OnNodeAdded(IWorkflowNodeViewModel node) => NodeAdded?.Invoke(Component, node);
    protected virtual void OnNodeRemoved(IWorkflowNodeViewModel node) => NodeRemoved?.Invoke(Component, node);

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) return;
                foreach (var item in e.NewItems)
                {
                    if (item is IWorkflowLinkViewModel link)
                    {
                        OnLinkAdded(link);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) return;
                foreach (var item in e.OldItems)
                {
                    if (item is IWorkflowLinkViewModel link)
                    {
                        OnLinkRemoved(link);
                    }
                }
                break;
        }
    }
    protected virtual void OnLinkAdded(IWorkflowLinkViewModel link) => LinkAdded?.Invoke(Component, link);
    protected virtual void OnLinkRemoved(IWorkflowLinkViewModel link) => LinkRemoved?.Invoke(Component, link);
    #endregion

    #region Connection Manager   
    public virtual bool ValidateConnection(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver)
        => true;

    public virtual void SendConnection(IWorkflowSlotViewModel slot)
        => Component?.StandardSendConnection(slot);

    public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
        => Component?.StandardReceiveConnection(slot);

    public virtual void ResetVirtualLink()
        => Component?.StandardResetVirtualLink();
    #endregion

    #region Redo & Undo
    public virtual void Redo()
        => Component?.StandardRedo();

    public virtual void Submit(IWorkflowActionPair actionPair)
        => Component?.StandardSubmit(actionPair);

    public virtual void Undo()
        => Component?.StandardUndo();

    public virtual void ClearHistory()
        => Component?.StandardClearHistory();
    #endregion
}
