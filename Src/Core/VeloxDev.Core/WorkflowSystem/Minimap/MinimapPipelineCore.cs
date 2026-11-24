using System.Collections.Specialized;
using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The shared composition of a minimap: it groups the platform renderer, viewport and input layers and
/// supplies the defaults; every platform-specific decision stays in those layers.
/// </summary>
/// <typeparam name="TSurface">The platform drawing surface.</typeparam>
/// <typeparam name="TRenderer">The platform renderer implementation.</typeparam>
/// <typeparam name="TViewport">The platform viewport implementation.</typeparam>
/// <typeparam name="TInput">The platform input implementation.</typeparam>
public abstract class MinimapHostCore<TSurface, TRenderer, TViewport, TInput>
    where TRenderer : class, IMinimapRenderer<TSurface>
    where TViewport : class, IMinimapViewportHost
    where TInput : class, IMinimapInputHost
{
    /// <summary>Creates the host from its platform layers.</summary>
    /// <param name="renderer">The layer that draws one minimap pass.</param>
    /// <param name="viewport">The layer that reports and applies scroll state.</param>
    /// <param name="input">The layer that reports pointer state and owns capture.</param>
    /// <exception cref="ArgumentNullException">A required layer is <see langword="null"/>.</exception>
    protected MinimapHostCore(TRenderer renderer, TViewport viewport, TInput input)
    {
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <inheritdoc cref="IMinimapRenderer{TSurface}"/>
    public TRenderer Renderer { get; }

    /// <inheritdoc cref="IMinimapViewportHost"/>
    public TViewport Viewport { get; }

    /// <inheritdoc cref="IMinimapInputHost"/>
    public TInput Input { get; }
}

/// <summary>
/// The platform-neutral minimap state machine: it owns tree subscription, change tracking, dirty-flag
/// debouncing, content and viewport mapping, placement, viewport-block drag math, and the renderer call.
/// Adapters derive from it once and expose the closed type under the name their users see.
/// </summary>
/// <typeparam name="TSurface">The platform drawing surface.</typeparam>
/// <typeparam name="TRenderer">The platform renderer implementation.</typeparam>
/// <typeparam name="TViewport">The platform viewport implementation.</typeparam>
/// <typeparam name="TInput">The platform input implementation.</typeparam>
/// <remarks>
/// <para>
/// Node, link and viewport changes only set the dirty flag and raise <see cref="RenderRequested"/>; the
/// mapping and placement work runs once on the next <see cref="Render"/> or <see cref="Refresh"/>. That is
/// the debounce: a burst of property changes costs one mapping pass.
/// </para>
/// <para>
/// Tree notifications may arrive from any thread. The pipeline itself is not thread-safe: the adapter must
/// marshal <see cref="Invalidate"/> and render calls to the host thread, as it does for the surface.
/// </para>
/// </remarks>
public class MinimapPipelineCore<TSurface, TRenderer, TViewport, TInput>
    : MinimapHostCore<TSurface, TRenderer, TViewport, TInput>, IDisposable
    where TRenderer : class, IMinimapRenderer<TSurface>
    where TViewport : class, IMinimapViewportHost
    where TInput : class, IMinimapInputHost
{
    private readonly HashSet<INodeComponent> subscribedNodes = [];
    private readonly HashSet<ILinkComponent> subscribedLinks = [];
    private ITreeComponent? tree;
    private MinimapDrawContext? drawContext;
    private MinimapMetrics metrics;
    private bool dirty = true;
    private bool dragging;
    private bool disposed;

    /// <summary>Composes the pipeline from its platform layers.</summary>
    /// <param name="renderer">The layer that draws one minimap pass.</param>
    /// <param name="viewport">The layer that reports and applies scroll state.</param>
    /// <param name="input">The layer that reports pointer state and owns capture.</param>
    /// <param name="metrics">The initial sizing and placement inputs; defaults to <see cref="MinimapMetrics.Default"/>.</param>
    /// <exception cref="ArgumentNullException">A required layer is <see langword="null"/>.</exception>
    public MinimapPipelineCore(
        TRenderer renderer,
        TViewport viewport,
        TInput input,
        MinimapMetrics? metrics = null)
        : base(renderer, viewport, input)
    {
        this.metrics = metrics ?? MinimapMetrics.Default;
    }

    /// <summary>Raised when a change made the cached geometry stale; the adapter translates it into its invalidate call.</summary>
    public event EventHandler? RenderRequested;

    /// <summary>Gets or sets the sizing and placement inputs; assigning a new value marks the geometry stale.</summary>
    public MinimapMetrics Metrics
    {
        get => metrics;
        set
        {
            if (metrics.Equals(value))
            {
                return;
            }

            metrics = value;
            dirty = true;
        }
    }

    /// <summary>Gets or sets whether a render pass draws anything. Hit-testing and drag math still work while hidden.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Gets or sets the tree the minimap renders; assigning unsubscribes the previous tree and subscribes the new one.</summary>
    public ITreeComponent? Tree
    {
        get => tree;
        set
        {
            if (ReferenceEquals(tree, value))
            {
                return;
            }

            UnsubscribeFromTree();
            tree = value;
            SubscribeToTree();
            Invalidate();
        }
    }

    /// <summary>Gets whether the cached mapping is stale and will be recomputed on the next <see cref="Refresh"/>.</summary>
    public bool IsDirty => dirty;

    /// <summary>Gets whether the viewport block is currently being dragged.</summary>
    public bool IsDragging => dragging;

    /// <summary>Gets the canvas-local bounds covering every node from the last mapping pass.</summary>
    public WorkflowBounds ContentBounds { get; private set; }

    /// <summary>Gets the canvas-local viewport rectangle from the last mapping pass.</summary>
    public WorkflowBounds ViewportBounds { get; private set; }

    /// <summary>Gets the geometry of the last mapping pass, or <see langword="null"/> when there is nothing to draw.</summary>
    public MinimapDrawContext? DrawContext => drawContext;

    /// <summary>Gets the number of node views currently tracked for change notifications.</summary>
    public int SubscribedNodeCount => subscribedNodes.Count;

    /// <summary>Gets the number of link views currently tracked for change notifications.</summary>
    public int SubscribedLinkCount => subscribedLinks.Count;

    /// <summary>
    /// Gets the placed minimap rectangle for the current <see cref="Metrics"/>: its top-right corner sits at
    /// the available corner, and its size is clamped to the free area and the minimum size.
    /// </summary>
    public WorkflowBounds MinimapRect
    {
        get
        {
            var placement = new MinimapDrawContext
            {
                MinimapWidth = Metrics.Width,
                MinimapHeight = Metrics.Height,
                MinimapMargin = Metrics.Margin,
                AvailableRight = Metrics.AvailableRight,
                AvailableTop = Metrics.AvailableTop,
                AvailableBottom = Metrics.AvailableBottom,
            };
            return placement.GetMinimapRect(Metrics.MinSize);
        }
    }

    /// <summary>Marks the cached geometry stale and asks the adapter to schedule a render.</summary>
    public void Invalidate()
    {
        if (disposed)
        {
            return;
        }

        dirty = true;
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recomputes the cached mapping when it is stale; otherwise does nothing.</summary>
    public void Refresh()
    {
        if (disposed || !dirty)
        {
            return;
        }

        dirty = false;
        drawContext = BuildDrawContext();
    }

    /// <summary>Refreshes the cached mapping and hands it to the renderer layer when the minimap is visible and has content.</summary>
    /// <param name="surface">The platform drawing surface for this pass.</param>
    public void Render(TSurface surface)
    {
        if (disposed || !IsVisible)
        {
            return;
        }

        Refresh();
        if (drawContext is null)
        {
            return;
        }

        Renderer.Render(surface, drawContext);
    }

    /// <summary>Tests whether an overlay-local point lies inside the interactive minimap rectangle.</summary>
    /// <param name="x">The overlay-local x coordinate.</param>
    /// <param name="y">The overlay-local y coordinate.</param>
    /// <returns><see langword="true"/> when the point is inside the minimap.</returns>
    public bool HitTest(double x, double y) => Contains(InteractionRect(), x, y);

    /// <summary>
    /// Starts a drag when the pointer is pressed inside the interactive rectangle: it centers the viewport on
    /// the pressed point, marks the drag active, and captures the pointer.
    /// </summary>
    /// <returns><see langword="true"/> when the press started a drag on the minimap.</returns>
    public bool PointerPressed()
    {
        if (disposed || dragging || !Input.IsPressed)
        {
            return false;
        }

        var (x, y) = Input.PointerPosition;
        if (!HitTest(x, y))
        {
            return false;
        }

        Refresh();
        NavigateTo(x, y);
        dragging = true;
        Input.Capture();
        return true;
    }

    /// <summary>Navigates to the current pointer position while a drag is active; otherwise does nothing.</summary>
    public void PointerMoved()
    {
        if (disposed || !dragging)
        {
            return;
        }

        var (x, y) = Input.PointerPosition;
        NavigateTo(x, y);
    }

    /// <summary>Ends an active drag and releases the pointer.</summary>
    public void PointerReleased()
    {
        if (!dragging)
        {
            return;
        }

        dragging = false;
        Input.Release();
    }

    /// <summary>Ends an active drag without asking for capture again; use it when the platform already lost capture.</summary>
    public void PointerCaptureLost()
    {
        if (!dragging)
        {
            return;
        }

        dragging = false;
        Input.Release();
    }

    /// <summary>Stops tracking the tree and releases every subscription.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        UnsubscribeFromTree();
        tree = null;
        drawContext = null;
    }

    private WorkflowBounds InteractionRect()
    {
        var clamp = Input.ClampRect;
        return clamp.IsEmpty ? MinimapRect : clamp;
    }

    private static bool Contains(WorkflowBounds rect, double x, double y)
        => !rect.IsEmpty && x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;

    private MinimapDrawContext? BuildDrawContext()
    {
        if (tree?.Nodes is null)
        {
            ContentBounds = default;
            ViewportBounds = default;
            return null;
        }

        var nodeRects = new List<(double X, double Y, double W, double H)>(tree.Nodes.Count);
        foreach (var node in tree.Nodes)
        {
            nodeRects.Add((node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height));
        }

        ContentBounds = WorkflowBounds.FromNodes(nodeRects);

        // 视口换算与 WPF 旧实现一致：scroll − contentOffset 得到画布局部坐标。
        var vw = Math.Max(1, Viewport.ViewportWidth);
        var vh = Math.Max(1, Viewport.ViewportHeight);
        ViewportBounds = WorkflowBounds.FromNode(
            WorkflowSurfaceMath.ToCanvasLocal(Viewport.ScrollOffsetX, Viewport.ContentOffsetX),
            WorkflowSurfaceMath.ToCanvasLocal(Viewport.ScrollOffsetY, Viewport.ContentOffsetY),
            vw, vh);

        if (ContentBounds.IsEmpty)
        {
            return null;
        }

        var placement = MinimapRect;
        var mmW = Math.Max(1, placement.Width);
        var mmH = Math.Max(1, placement.Height);
        var pad = Math.Max(0, Metrics.Padding);
        var (ox, oy, sc) = WorkflowSurfaceMath.MinimapFit(
            ContentBounds.Width, ContentBounds.Height, mmW - pad * 2, mmH - pad * 2, pad);

        var nodes = new List<WorkflowBounds>(nodeRects.Count);
        var mapped = new List<WorkflowBounds>(nodeRects.Count);
        var minThumb = Math.Max(0, Metrics.MinimumThumbSize);
        foreach (var (nx, ny, nw, nh) in nodeRects)
        {
            nodes.Add(WorkflowBounds.FromNode(nx, ny, nw, nh));
            var (l, t) = WorkflowSurfaceMath.MinimapLocal(
                nx, ny, ContentBounds.Left, ContentBounds.Top, ox, oy, sc);
            mapped.Add(WorkflowBounds.FromNode(
                l, t,
                WorkflowSurfaceMath.MinThumbSize(nw, sc, minThumb),
                WorkflowSurfaceMath.MinThumbSize(nh, sc, minThumb)));
        }

        var mappedViewport = default(WorkflowBounds);
        if (!ViewportBounds.IsEmpty)
        {
            var (vx, vy, vwid, vhei) = WorkflowSurfaceMath.MinimapViewportRect(
                ox, oy, sc,
                ViewportBounds.Left, ViewportBounds.Top, ViewportBounds.Width, ViewportBounds.Height,
                ContentBounds.Left, ContentBounds.Top, mmW, mmH, minRectSize: 2d);
            mappedViewport = WorkflowBounds.FromNode(vx, vy, vwid, vhei);
        }

        return new MinimapDrawContext
        {
            Tree = tree,
            ContentBounds = ContentBounds,
            ViewportBounds = ViewportBounds,
            Nodes = nodes,
            MappedNodes = mapped,
            MappedViewport = mappedViewport,
            MinimapWidth = mmW,
            MinimapHeight = mmH,
            Padding = pad,
            OriginX = ox,
            OriginY = oy,
            Scale = sc,
            NodeCornerRadius = Math.Max(0, Metrics.NodeCornerRadius),
            MinimapCornerRadius = Math.Max(0, Metrics.MinimapCornerRadius),
            AvailableRight = Metrics.AvailableRight,
            AvailableTop = Metrics.AvailableTop,
            AvailableBottom = Metrics.AvailableBottom,
            MinimapMargin = Metrics.Margin,
        };
    }

    private void NavigateTo(double overlayX, double overlayY)
    {
        var context = drawContext;
        if (context is null || tree?.Layout is not { } layout)
        {
            return;
        }

        var rect = MinimapRect;
        var mmX = overlayX - rect.Left;
        var mmY = overlayY - rect.Top;
        if (context.Scale <= 0d)
        {
            return;
        }

        var (wcx, wcy) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            mmX, mmY, context.OriginX, context.OriginY, context.Scale,
            ContentBounds.Left, ContentBounds.Top);
        var (scrollX, scrollY) = WorkflowSurfaceMath.MinimapToScroll(
            wcx, wcy, Viewport.ViewportWidth, Viewport.ViewportHeight,
            Viewport.ContentOffsetX, Viewport.ContentOffsetY);

        var maxH = Math.Max(0, Viewport.ScrollableWidth);
        var maxV = Math.Max(0, Viewport.ScrollableHeight);
        scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true);
        scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false);

        Viewport.ApplyScroll(
            WorkflowSurfaceMath.ClampValue(scrollX, 0, maxH),
            WorkflowSurfaceMath.ClampValue(scrollY, 0, maxV));
    }

    // ── 树订阅 ────────────────────────────────────────────────────────────────

    private void SubscribeToTree()
    {
        if (tree is null)
        {
            return;
        }

        if (tree.Nodes is INotifyCollectionChanged nodes)
        {
            nodes.CollectionChanged += OnNodesChanged;
            foreach (var node in tree.Nodes)
            {
                SubscribeNode(node);
            }
        }

        if (tree.Links is INotifyCollectionChanged links)
        {
            links.CollectionChanged += OnLinksChanged;
            foreach (var link in tree.Links)
            {
                SubscribeLink(link);
            }
        }
    }

    private void UnsubscribeFromTree()
    {
        if (tree is not null)
        {
            if (tree.Nodes is INotifyCollectionChanged nodes)
            {
                nodes.CollectionChanged -= OnNodesChanged;
            }

            if (tree.Links is INotifyCollectionChanged links)
            {
                links.CollectionChanged -= OnLinksChanged;
            }
        }

        foreach (var node in subscribedNodes)
        {
            if (node is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        subscribedNodes.Clear();

        foreach (var link in subscribedLinks)
        {
            UnsubscribeLinkSlots(link);
        }

        subscribedLinks.Clear();
    }

    private void SubscribeNode(INodeComponent node)
    {
        if (subscribedNodes.Add(node) && node is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void UnsubscribeNode(INodeComponent node)
    {
        if (subscribedNodes.Remove(node) && node is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void SubscribeLink(ILinkComponent link)
    {
        if (!subscribedLinks.Add(link))
        {
            return;
        }

        if (link.Sender is INotifyPropertyChanged sender)
        {
            sender.PropertyChanged += OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged receiver)
        {
            receiver.PropertyChanged += OnSlotPropertyChanged;
        }
    }

    private void UnsubscribeLink(ILinkComponent link)
    {
        if (subscribedLinks.Remove(link))
        {
            UnsubscribeLinkSlots(link);
        }
    }

    private void UnsubscribeLinkSlots(ILinkComponent link)
    {
        if (link.Sender is INotifyPropertyChanged sender)
        {
            sender.PropertyChanged -= OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged receiver)
        {
            receiver.PropertyChanged -= OnSlotPropertyChanged;
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var node in subscribedNodes.ToArray())
            {
                UnsubscribeNode(node);
            }

            if (tree?.Nodes is not null)
            {
                foreach (var node in tree.Nodes)
                {
                    SubscribeNode(node);
                }
            }
        }
        else
        {
            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is INodeComponent node)
                    {
                        SubscribeNode(node);
                    }
                }
            }

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is INodeComponent node)
                    {
                        UnsubscribeNode(node);
                    }
                }
            }
        }

        Invalidate();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var link in subscribedLinks.ToArray())
            {
                UnsubscribeLink(link);
            }

            if (tree?.Links is not null)
            {
                foreach (var link in tree.Links)
                {
                    SubscribeLink(link);
                }
            }
        }
        else
        {
            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is ILinkComponent link)
                    {
                        SubscribeLink(link);
                    }
                }
            }

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is ILinkComponent link)
                    {
                        UnsubscribeLink(link);
                    }
                }
            }
        }

        Invalidate();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size))
        {
            Invalidate();
        }
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISlotComponent.Anchor))
        {
            Invalidate();
        }
    }
}
