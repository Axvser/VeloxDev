using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The WPF side of a node drag-in: a full-viewport layer that draws the draft on the surface decorator,
/// bypassing the view pool and the ViewModel→view matching entirely.
/// </summary>
/// <remarks>
/// <para>
/// Place it inside the surface decorator (typically as the last child, above the content) and derive from it.
/// The base finds the nearest <see cref="IWorkflowGridDecorator"/> ancestor, or uses the one handed to
/// <see cref="IWorkflowNodeDropSurface{TContext}.Begin"/>, and maps the draft's collapsed anchor into that
/// decorator's viewport with <see cref="WorkflowSurfaceMath.GridX"/> / <see cref="WorkflowSurfaceMath.GridY"/>.
/// The draft's <see cref="IWorkflowNodeViewModel.Size"/> is a world size like every other node's, so it is
/// divided by the tree's scale before it reaches <see cref="DrawNode"/>; the tree comes from
/// <see cref="Tree"/> or, when that is unset, from <see cref="FrameworkElement.DataContext"/>.
/// </para>
/// <para>
/// Only <see cref="DrawNode"/> is yours to write. The base invalidates on every pointer move, on the
/// decorator's layout (scroll) and on the tree's layout property changes (zoom), and it never touches
/// <c>VisibleItems</c>, the view pool or a data template.
/// </para>
/// </remarks>
public abstract class NodeDropSurface : FrameworkElement, IWorkflowNodeDropSurface<IWorkflowGridDecorator>
{
    private IWorkflowNodeViewModel? node;
    private IWorkflowGridDecorator? decorator;
    private FrameworkElement? decoratorElement;
    private IWorkflowTreeViewModel? tree;
    private IWorkflowTreeViewModel? treeOverride;
    private WorkflowNodeDrop? attachedDrop;

    /// <summary>Creates a hit-test transparent preview layer.</summary>
    protected NodeDropSurface()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Whether a draft is currently being drawn.</summary>
    public bool IsActive => node is not null;

    /// <summary>The draft being drawn, or <see langword="null"/> when idle.</summary>
    public IWorkflowNodeViewModel? Node => node;

    /// <summary>The decorator whose viewport this surface draws in, or <see langword="null"/> while idle.</summary>
    public IWorkflowGridDecorator? Decorator => decorator;

    /// <summary>
    /// The tree that supplies <see cref="CanvasLayout.Scale"/>. Omitted, the surface reads
    /// <see cref="FrameworkElement.DataContext"/> — which a workflow host normally sets to the tree.
    /// </summary>
    public IWorkflowTreeViewModel? Tree
    {
        get => treeOverride;
        set
        {
            if (ReferenceEquals(treeOverride, value)) return;
            treeOverride = value;
            if (IsActive)
            {
                AttachTree();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Starts a drag on <paramref name="drop"/> with this surface drawing it. The decorator is the one handed
    /// in, else the nearest <see cref="IWorkflowGridDecorator"/> ancestor.
    /// </summary>
    /// <param name="drop">The tree's drag-in state.</param>
    /// <param name="node">The unattached draft node.</param>
    /// <param name="pointer">The initial pointer position, in the same collapsed frame the input route uses.</param>
    /// <exception cref="InvalidOperationException">
    /// The surface is neither inside a decorator nor given one, so there is nowhere to draw.
    /// </exception>
    public void BeginDrop(WorkflowNodeDrop drop, IWorkflowNodeViewModel node, Anchor? pointer = null)
    {
        if (drop is null) throw new ArgumentNullException(nameof(drop));

        var target = decorator ?? FindDecorator()
            ?? throw new InvalidOperationException(
                "NodeDropSurface must sit inside the surface decorator, or be given one, before a drag starts.");

        // 换一个 drop = 放弃上一个：否则上一个稍后的 End 会把这一份新草稿从画面上清掉。
        if (attachedDrop is { IsActive: true } previous && !ReferenceEquals(previous, drop))
            previous.Cancel();

        attachedDrop = drop;
        drop.Begin(node, this, target, pointer);
    }

    /// <inheritdoc />
    public void Begin(IWorkflowNodeViewModel node, IWorkflowGridDecorator context)
    {
        this.node = node ?? throw new ArgumentNullException(nameof(node));
        Attach(context);
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void Move(IWorkflowNodeViewModel node, IWorkflowGridDecorator context)
    {
        this.node = node ?? throw new ArgumentNullException(nameof(node));
        Attach(context);
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void End(IWorkflowNodeViewModel node, IWorkflowGridDecorator context, bool committed)
    {
        Detach();
        this.node = null;
        attachedDrop = null;
        InvalidateVisual();
    }

    /// <summary>
    /// The draft's viewport bounds, or <see langword="null"/> when idle or when no decorator is known. The same
    /// rectangle is handed to <see cref="DrawNode"/>.
    /// </summary>
    public Rect? NodeBounds
        => node is { } draft && decorator is { } target ? ComputeBounds(draft, target) : null;

    /// <summary>Draws the draft. The context carries the viewport origin, the collapsed size and the decorator.</summary>
    /// <param name="context">The draft and the viewport geometry to draw it with.</param>
    protected abstract void DrawNode(NodeDropDrawingContext context);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (node is not { } draft || decorator is not { } target) return;

        var bounds = ComputeBounds(draft, target);
        var scale = ScaleOf();
        DrawNode(new NodeDropDrawingContext(
            drawingContext, draft, target, bounds.TopLeft, bounds.Width, bounds.Height, scale.X, scale.Y));
    }

    private Rect ComputeBounds(IWorkflowNodeViewModel draft, IWorkflowGridDecorator target)
    {
        var scale = ScaleOf();
        var worldLeft = WorkflowSurfaceMath.GridWorldLeft(target.ScrollOffsetX, target.ContentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridWorldTop(target.ScrollOffsetY, target.ContentOffsetY);

        var x = WorkflowSurfaceMath.GridX(draft.Anchor.Horizontal, worldLeft, target.RulerBand);
        var y = WorkflowSurfaceMath.GridY(draft.Anchor.Vertical, worldTop, target.RulerBand);

        // Size 与真实节点一样是世界值；视图里的卡片是折叠后的，所以按缩放折一次。
        return new Rect(x, y, draft.Size.Width / scale.X, draft.Size.Height / scale.Y);
    }

    private (double X, double Y) ScaleOf()
    {
        var scale = tree?.Layout?.Scale;
        return (
            scale is null || scale.Horizontal == 0d ? 1d : scale.Horizontal,
            scale is null || scale.Vertical == 0d ? 1d : scale.Vertical);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!IsActive) return;
        AttachTree();
        InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 表面离开可视树时把草稿交还给 drop：否则换视图/换树之后 Core 仍以为这次拖入在进行。
        if (IsActive) attachedDrop?.Cancel();
        attachedDrop = null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsActive) return;
        AttachTree();
        InvalidateVisual();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (IsActive) InvalidateVisual();
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsActive) InvalidateVisual();
    }

    private void Attach(IWorkflowGridDecorator? context)
    {
        var next = context ?? FindDecorator();
        if (next is null) return;

        if (!ReferenceEquals(decorator, next))
        {
            if (decoratorElement is not null) decoratorElement.LayoutUpdated -= OnLayoutUpdated;
            decorator = next;
            decoratorElement = next as FrameworkElement;
            if (decoratorElement is not null) decoratorElement.LayoutUpdated += OnLayoutUpdated;
        }

        AttachTree();
    }

    private void AttachTree()
    {
        var next = treeOverride ?? DataContext as IWorkflowTreeViewModel;
        if (ReferenceEquals(tree, next)) return;

        if (tree?.Layout is INotifyPropertyChanged previous) previous.PropertyChanged -= OnLayoutPropertyChanged;
        tree = next;
        if (tree?.Layout is INotifyPropertyChanged current) current.PropertyChanged += OnLayoutPropertyChanged;
    }

    private void Detach()
    {
        if (decoratorElement is not null)
        {
            decoratorElement.LayoutUpdated -= OnLayoutUpdated;
            decoratorElement = null;
        }

        if (tree?.Layout is INotifyPropertyChanged current) current.PropertyChanged -= OnLayoutPropertyChanged;

        decorator = null;
        tree = null;
    }

    private IWorkflowGridDecorator? FindDecorator()
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is IWorkflowGridDecorator found) return found;
        }

        return null;
    }
}

/// <summary>
/// What <see cref="NodeDropSurface.DrawNode"/> receives: the viewport rectangle the draft occupies, its node,
/// the decorator, and the live <see cref="System.Windows.Media.DrawingContext"/>.
/// </summary>
public readonly struct NodeDropDrawingContext
{
    internal NodeDropDrawingContext(
        DrawingContext drawingContext,
        IWorkflowNodeViewModel node,
        IWorkflowGridDecorator decorator,
        Point origin,
        double width,
        double height,
        double scaleX,
        double scaleY)
    {
        DrawingContext = drawingContext;
        Node = node;
        Decorator = decorator;
        Origin = origin;
        Width = width;
        Height = height;
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    /// <summary>The WPF drawing context for this render pass. It is only valid inside the draw call.</summary>
    public DrawingContext DrawingContext { get; }

    /// <summary>The draft node.</summary>
    public IWorkflowNodeViewModel Node { get; }

    /// <summary>The decorator whose viewport coordinates <see cref="Origin"/> uses.</summary>
    public IWorkflowGridDecorator Decorator { get; }

    /// <summary>The draft's top-left in viewport coordinates.</summary>
    public Point Origin { get; }

    /// <summary>The draft's collapsed width, in viewport pixels.</summary>
    public double Width { get; }

    /// <summary>The draft's collapsed height, in viewport pixels.</summary>
    public double Height { get; }

    /// <summary>The tree's horizontal scale, for drawing world-sized details.</summary>
    public double ScaleX { get; }

    /// <summary>The tree's vertical scale, for drawing world-sized details.</summary>
    public double ScaleY { get; }

    /// <summary>The draft's viewport rectangle — <see cref="Origin"/> and the collapsed size.</summary>
    public Rect Bounds => new(Origin.X, Origin.Y, Width, Height);
}
