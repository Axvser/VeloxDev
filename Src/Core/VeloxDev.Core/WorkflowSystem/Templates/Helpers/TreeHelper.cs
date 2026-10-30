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
public partial class TreeHelper<T> : IWorkflowTreeViewModelHelper, IWorkflowTreeEvents, IInputEvents, IWorkflowLayoutHost
    where T : class, IWorkflowTreeViewModel
{
    private IWorkflowLayoutSolver? layoutSolver;

    /// <inheritdoc />
    public InputRelay Input { get; } = new();

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
    private int isDirty;
    // 安装线程的同步上下文。可见集绑在 UI 上，tick 线程自己不能碰 —— 只能把这一趟送回宿主上下文执行。
    private volatile SynchronizationContext? hostContext;
    // 宿主显式设过（包括显式设成 null 表示「就地执行」）时，Install 不再猜。
    private bool hostContextSet;
    private int uiPassQueued;
    // 每次 Install 递增；投递时把版本一并带走，卸载/换线程重装后旧上下文里的回投才会被丢掉。
    private int installVersion;

    partial void Update(FrameEventArgs e)
    {
        if (!useVirtualization || Volatile.Read(ref isDirty) == 0) return;

        var context = hostContext;
        if (context is null || ReferenceEquals(context, SynchronizationContext.Current))
        {
            RunVirtualizationPass();
            return;
        }

        // 合并背压：UI 忙时只留一趟排队，tick 不会把它堆成一条长队。
        if (Interlocked.Exchange(ref uiPassQueued, 1) != 0) return;

        try
        {
            var version = Volatile.Read(ref installVersion);
            context.Post(_ => RunQueuedVirtualizationPass(version), null);
        }
        catch (Exception ex)
        {
            // 上下文关停（应用退出）时 Post 会抛。放回旗标，下一 tick 再试；这一帧不做事。
            Interlocked.Exchange(ref uiPassQueued, 0);
            Debug.WriteLine($"TreeHelper: failed to post the virtualization pass to the host context: {ex.Message}");
        }
    }

    private void RunQueuedVirtualizationPass(int version)
    {
        // 旧安装留下的回投：它盯的那一树已经换掉了，别拿新组件在旧线程上跑。
        if (version != Volatile.Read(ref installVersion)) return;

        Interlocked.Exchange(ref uiPassQueued, 0);
        RunVirtualizationPass();
    }

    private void RunVirtualizationPass()
    {
        // 先取走脏标记：这一趟执行期间的变更会重新标脏，由下一 tick 接手，不会被这里吞掉。
        if (Interlocked.Exchange(ref isDirty, 0) == 0) return;

        Component?.Virtualize(Viewport);
        BroadcastVisibleItemLayout();
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

        // 上下文跟着这一次安装走：每次安装都重新取安装线程的上下文（宿主显式指定的除外）。
        if (!hostContextSet) hostContext = SynchronizationContext.Current;
        Interlocked.Increment(ref installVersion);
        // 上一次安装可能留下没被消费的投递；新安装必须能重新排队，否则这一树永远等不到虚拟化。
        Interlocked.Exchange(ref uiPassQueued, 0);

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
        // 自动捕获的上下文属于这一次安装；卸载后不再持有，免得移除 tickable 生效前还往旧 UI 投递。
        if (!hostContextSet) hostContext = null;
        Interlocked.Exchange(ref uiPassQueued, 0);
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

    /// <summary>
    /// Binds the virtualization passes that <see cref="MarkDirty"/> schedules to <paramref name="context"/>, the
    /// host's UI context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A virtualizing helper rebuilds <see cref="VisibleItems"/> and re-raises node layout notifications, both of
    /// which are normally bound to UI. Its tick runs on a timer thread, so the pass is posted to this context rather
    /// than executed there. Every <see cref="Install"/> captures
    /// <see cref="SynchronizationContext.Current"/> afresh when nothing was set explicitly, so a helper moved to a
    /// different tree is rebound to that installation's thread; <see cref="Uninstall"/> drops the captured context.
    /// </para>
    /// <para>
    /// Pass null for a host with no UI context (a service, a headless test): the pass then runs inline on the tick
    /// thread, as it always did. An explicit value — null included — survives later installations.
    /// </para>
    /// </remarks>
    /// <param name="context">The context the visible set may be touched on, or null to run inline.</param>
    /// <returns>This helper, so the call can be chained onto a constructor.</returns>
    public TreeHelper<T> WithSynchronizationContext(SynchronizationContext? context)
    {
        hostContextSet = true;
        hostContext = context;
        return this;
    }

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
                        Interlocked.Exchange(ref isDirty, 1);
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
                        Interlocked.Exchange(ref isDirty, 1);
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
                        Interlocked.Exchange(ref isDirty, 1);
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
                        Interlocked.Exchange(ref isDirty, 1);
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
    public virtual void MarkDirty() => Interlocked.Exchange(ref isDirty, 1);
    #endregion

    #region Layout
    /// <summary>
    /// The layout solution registered for this tree; the default separation solver until replaced.
    /// </summary>
    /// <remarks>
    /// One helper belongs to one tree (one node editor), so a replacement here never leaks into another
    /// editor. Custom solutions can replace the whole solver, or construct
    /// <see cref="DefaultWorkflowLayoutSolver"/> with a different planner/cost.
    /// </remarks>
    public virtual IWorkflowLayoutSolver LayoutSolver
    {
        get => layoutSolver ??= new DefaultWorkflowLayoutSolver();
        set => layoutSolver = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public virtual WorkflowLayoutResult Arrange(
        IWorkflowNodeViewModel? start, WorkflowLayoutOptions? options = null)
    {
        if (Component is not { } tree)
        {
            throw new InvalidOperationException("The tree helper is not installed on a tree.");
        }

        options ??= new WorkflowLayoutOptions();
        if (start is not null) options.StartNode = start;
        return LayoutSolver.Arrange(tree, options);
    }
    #endregion
}
