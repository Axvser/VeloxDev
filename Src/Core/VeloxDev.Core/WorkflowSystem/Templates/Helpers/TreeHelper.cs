using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
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
public partial class TreeHelper<T> : IWorkflowTreeViewModelHelper
    where T : class, IWorkflowTreeViewModel
{
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    [VeloxProperty] private ObservableCollection<IWorkflowViewModel> visibleItems = [];
    partial void OnItemAddedToVisibleItems(IEnumerable<IWorkflowViewModel> items)
    {
        foreach (var item in items)
        {
            OnVisibleItemsAdded(item);
        }
    }
    partial void OnItemRemovedFromVisibleItems(IEnumerable<IWorkflowViewModel> items)
    {
        foreach (var item in items)
        {
            OnVisibleItemsRemoved(item);
        }
    }
    protected virtual void OnVisibleItemsAdded(IWorkflowViewModel visibleItem) => VisibleItemAdded?.Invoke(this, visibleItem);
    protected virtual void OnVisibleItemsRemoved(IWorkflowViewModel visibleItem) => VisibleItemRemoved?.Invoke(this, visibleItem);

    [VeloxProperty] private Viewport viewport = new();
    partial void OnViewportChanged(Viewport oldValue, Viewport newValue)
    {
        Component?.Virtualize(newValue);
    }

    public event EventHandler<IWorkflowNodeViewModel>? NodeAdded;
    public event EventHandler<IWorkflowNodeViewModel>? NodeRemoved;
    public event EventHandler<IWorkflowLinkViewModel>? LinkAdded;
    public event EventHandler<IWorkflowLinkViewModel>? LinkRemoved;
    public event EventHandler<IWorkflowViewModel>? VisibleItemAdded;
    public event EventHandler<IWorkflowViewModel>? VisibleItemRemoved;

    public virtual void Install(IWorkflowTreeViewModel tree)
    {
        Component = tree as T;
        commands = tree.GetStandardCommands();
        VisibleItems = [];
        tree.Nodes.CollectionChanged += OnNodesChanged;
        tree.Links.CollectionChanged += OnLinksChanged;
        if (Component is null || tree.EnableMap(200, VisibleItems) < 0)
        {
            Debug.Fail("EnableMap did not return a non-negative value as expected. Please check the implementation of EnableMap in the IWorkflowTreeViewModel.");
        }
    }

    public virtual void Uninstall(IWorkflowTreeViewModel tree)
    {
        Component = null;
        commands = [];
        tree.Nodes.CollectionChanged -= OnNodesChanged;
        tree.Links.CollectionChanged -= OnLinksChanged;
        if (tree.ClearMap() != 5)
        {
            Debug.WriteLine("ClearMap did not return 5 as expected. Please check the implementation of ClearMap in the IWorkflowTreeViewModel.");
        }
        VisibleItems.Clear();
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

    public virtual void Virtualize(Viewport viewport)
        => Component?.Virtualize(viewport);

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
        Component?.Virtualize(Viewport);
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
        Component?.Virtualize(Viewport);
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
