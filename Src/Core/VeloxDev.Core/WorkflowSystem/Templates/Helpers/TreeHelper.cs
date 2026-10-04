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
    /// <summary>Creates a helper with virtualization off.</summary>
    public TreeHelper() : base()
    {

    }

    /// <summary>Creates a helper with virtualization on.</summary>
    /// <param name="cellSize">The cell size of the spatial grid.</param>
    public TreeHelper(double cellSize) : base(cellSize)
    {

    }
}

/// <summary>
/// [ Component Helper ] Provide standard supports for Tree Component
/// </summary>
/// <typeparam name="T">The type of the Tree ViewModel that this helper is designed for.</typeparam>
[Tickable(channel: nameof(TreeHelper), fps: 10)]
public partial class TreeHelper<T> : IWorkflowTreeViewModelHelper, IWorkflowTreeEvents, IWorkflowInputEvents
    where T : class, IWorkflowTreeViewModel
{
    /// <inheritdoc />
    public WorkflowInputRelay Input { get; } = new();

    /// <summary>Creates a helper with virtualization off.</summary>
    public TreeHelper()
    {
        useVirtualization = false;
    }

    /// <summary>Creates a helper with virtualization on.</summary>
    /// <param name="cellSize">The cell size of the spatial grid.</param>
    public TreeHelper(double cellSize)
    {
        useVirtualization = true;
        CellSize = cellSize;
        if (!TickManager.IsRunning(nameof(TreeHelper)))
        {
            TickManager.Start(nameof(TreeHelper));
        }
    }

    /// <summary>The tree this helper is installed on, when it matches <typeparamref name="T"/>.</summary>
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
    /// <summary>Raises <see cref="VisibleItemAdded"/>.</summary>
    protected virtual void OnVisibleItemsAdded(IWorkflowViewModel visibleItem) => VisibleItemAdded?.Invoke(this, visibleItem);
    /// <summary>Raises <see cref="VisibleItemRemoved"/>.</summary>
    protected virtual void OnVisibleItemsRemoved(IWorkflowViewModel visibleItem) => VisibleItemRemoved?.Invoke(this, visibleItem);

    [VeloxProperty] private Viewport viewport = new();
    partial void OnViewportChanged(Viewport oldValue, Viewport newValue)
    {
        Component?.Virtualize(newValue);
    }

    /// <inheritdoc />
    public event EventHandler<IWorkflowNodeViewModel>? NodeAdded;
    /// <inheritdoc />
    public event EventHandler<IWorkflowNodeViewModel>? NodeRemoved;
    /// <inheritdoc />
    public event EventHandler<IWorkflowLinkViewModel>? LinkAdded;
    /// <inheritdoc />
    public event EventHandler<IWorkflowLinkViewModel>? LinkRemoved;
    /// <inheritdoc />
    public event EventHandler<IWorkflowViewModel>? VisibleItemAdded;
    /// <inheritdoc />
    public event EventHandler<IWorkflowViewModel>? VisibleItemRemoved;

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public virtual void Closing() => commands.StandardClosing();
    /// <inheritdoc />
    public virtual async Task CloseAsync()
    {
        if (Component is not null) await Component.StandardCloseAsync();
    }
    /// <inheritdoc />
    public virtual void Closed() => commands.StandardClosed();

    /// <inheritdoc />
    public virtual IWorkflowLinkViewModel CreateLink(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver)
        => new LinkDefaultViewModel()
        {
            Sender = sender,
            Receiver = receiver,
        };

    /// <inheritdoc />
    public virtual void CreateNode(IWorkflowNodeViewModel node)
        => Component?.StandardCreateNode(node);

    /// <inheritdoc />
    public virtual void SetPointer(Anchor anchor)
        => Component?.StandardSetPointer(anchor);

    /// <inheritdoc />
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
    /// <summary>Raises <see cref="NodeAdded"/>.</summary>
    protected virtual void OnNodeAdded(IWorkflowNodeViewModel node) => NodeAdded?.Invoke(Component, node);
    /// <summary>Raises <see cref="NodeRemoved"/>.</summary>
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
    /// <summary>Raises <see cref="LinkAdded"/>.</summary>
    protected virtual void OnLinkAdded(IWorkflowLinkViewModel link) => LinkAdded?.Invoke(Component, link);
    /// <summary>Raises <see cref="LinkRemoved"/>.</summary>
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

    /// <inheritdoc />
    public virtual bool ValidateConnection(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver)
        => true;

    /// <inheritdoc />
    public virtual void SendConnection(IWorkflowSlotViewModel slot)
        => Component?.StandardSendConnection(slot);

    /// <inheritdoc />
    public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
        => Component?.StandardReceiveConnection(slot);

    /// <inheritdoc />
    public virtual void ResetVirtualLink()
        => Component?.StandardResetVirtualLink();
    #endregion

    #region Redo & Undo
    /// <inheritdoc />
    public virtual void Redo()
        => Component?.StandardRedo();

    /// <inheritdoc />
    public virtual void Submit(IWorkflowActionPair actionPair)
        => Component?.StandardSubmit(actionPair);

    /// <inheritdoc />
    public virtual void Undo()
        => Component?.StandardUndo();

    /// <inheritdoc />
    public virtual void ClearHistory()
        => Component?.StandardClearHistory();

    /// <inheritdoc />
    public virtual void MarkDirty() => isDirty = true;
    #endregion
}
