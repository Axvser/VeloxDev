using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using VeloxDev.MVVM;
using VeloxDev.TimeLine;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// [ Component Helper ] Provide standard supports for Tree Component
/// </summary>
public class TreeHelper : TreeHelper<IWorkflowTreeViewModel>
{
    public TreeHelper() : base()
    {

    }

    public TreeHelper(double cellSize) : base(cellSize)
    {

    }
}

/// <summary>
/// [ Component Helper ] Provide standard supports for Tree Component
/// </summary>
/// <typeparam name="T">The type of the Tree ViewModel that this helper is designed for.</typeparam>
[Tickable(channel: nameof(TreeHelper), fps: 10)]
public partial class TreeHelper<T> : IWorkflowTreeViewModelHelper, IWorkflowTreeEvents
    where T : class, IWorkflowTreeViewModel
{
    public TreeHelper()
    {
        useVirtualization = false;
    }

    public TreeHelper(double cellSize)
    {
        useVirtualization = true;
        CellSize = cellSize;
        if (!TickManager.IsRunning(nameof(TreeHelper)))
        {
            TickManager.Start(nameof(TreeHelper));
        }
    }

    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];
    private double CellSize { get; } = 200;

    private readonly bool useVirtualization = false;
    private bool isDirty = false;

    partial void Update(FrameEventArgs e)
    {
        if (useVirtualization && isDirty)
        {
            Component?.Virtualize(Viewport);
            isDirty = false;
            BroadcastVisibleItemLayout();
        }
    }

    private void BroadcastVisibleItemLayout()
    {
        foreach (var item in VisibleItems)
        {
            if (item is IWorkflowNodeViewModel node)
            {
                node.OnPropertyChanged(nameof(node.Anchor));
                node.OnPropertyChanged(nameof(node.Size));
            }
        }
    }

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

        if (!useVirtualization) return;

        if (Component is null || tree.EnableMap(CellSize, VisibleItems) < 0)
        {
            Debug.Fail("EnableMap did not return a non-negative value as expected. Please check the implementation of EnableMap in the IWorkflowTreeViewModel.");
        }
        InitializeTickable();
    }

    public virtual void Uninstall(IWorkflowTreeViewModel tree)
    {
        Component = null;
        commands = [];
        tree.Nodes.CollectionChanged -= OnNodesChanged;
        tree.Links.CollectionChanged -= OnLinksChanged;

        if (!useVirtualization) return;

        if (tree.ClearMap() != 5)
        {
            Debug.WriteLine("ClearMap did not return 5 as expected. Please check the implementation of ClearMap in the IWorkflowTreeViewModel.");
        }
        VisibleItems.Clear();
        CloseTickable();
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
        => new LinkDefaultViewModel()
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
                        isDirty = true;
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
                        isDirty = true;
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
                        isDirty = true;
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
                        isDirty = true;
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
    /// <summary>
    /// Raised when a connection between two ports is about to be made — before <see cref="ValidateConnection"/> is
    /// asked. Refusing here is the per-drag answer a host can give without subclassing its Helper.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? Connecting;

    /// <summary>Raised once the link exists, carrying the same handle as <see cref="Connecting"/>.</summary>
    public event EventHandler<ConnectionEventArgs>? Connected;

    /// <summary>
    /// Asks the host whether this connection may be made, and hands back the handle the caller passes on to
    /// <see cref="RaiseConnected"/>.
    /// </summary>
    /// <param name="sender">The port the connection leaves.</param>
    /// <param name="receiver">The port it arrives at.</param>
    public virtual WorkflowEventHandle RaiseConnecting(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        var handle = new WorkflowEventHandle();
        Connecting?.Invoke(Component, new ConnectionEventArgs(sender, receiver, handle));
        return handle;
    }

    /// <summary>Reports a connection that happened, unless the handle silenced it.</summary>
    /// <param name="sender">The port the connection leaves.</param>
    /// <param name="receiver">The port it arrives at.</param>
    /// <param name="handle">The handle <see cref="RaiseConnecting"/> returned.</param>
    public virtual void RaiseConnected(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, WorkflowEventHandle handle)
    {
        if (handle.StopPropagation) return;
        Connected?.Invoke(Component, new ConnectionEventArgs(sender, receiver, handle));
    }

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

    public virtual void MarkDirty() => isDirty = true;
    #endregion
}
