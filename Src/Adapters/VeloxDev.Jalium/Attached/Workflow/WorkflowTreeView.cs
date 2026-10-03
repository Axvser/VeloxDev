using System;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The Jalium workflow surface: a self-drawn node-editor canvas that materializes node and link views from the
/// tree's visible set, pans and zooms the world, and drives the connection gesture.
/// </summary>
/// <remarks>
/// <para>
/// The world model is fixed node coordinates plus a layout offset: content is shifted by
/// <see cref="CanvasLayout.ActualOffset"/>, growing left/up by increasing the origin and right/down by widening.
/// The canvas itself is sized to the world extent, so the grid and the pooled views scroll with it, while the
/// ruler bands stay viewport-fixed.
/// </para>
/// <para>
/// Zoom is host-driven — the composing window owns its wheel handling and calls <see cref="NotifyZoomCommitted()"/>
/// once the scale and the offsets have settled. Derive from it for the palette and the item selector; the pooling,
/// the viewport bookkeeping, the gestures and the rendering are the same in every host.
/// </para>
/// </remarks>
public class WorkflowTreeView : Canvas
{
    /// <summary>Lower bound for the canvas size, so a nearly-empty tree still has a pannable surface.</summary>
    public const double CanvasWidth = 2000;

    /// <summary>The vertical counterpart of <see cref="CanvasWidth"/>.</summary>
    public const double CanvasHeight = 2000;

    private IWorkflowTreeViewModel? _tree;
    private ScrollViewer? _scrollViewer;

    /// <summary>
    /// Committed zoom scroll target.
    /// </summary>
    /// <remarks>
    /// Jalium's <c>ScrollTo</c> can land asynchronously, so a scroll report fired before the offset settles would
    /// rewrite <see cref="IWorkflowTreeViewModelHelper.Viewport"/> from a stale, pre-zoom offset — and the next
    /// virtualization would cull the freshly materialized links while the endpoint nodes (which enter the pool by
    /// their own rects) stayed. While the pin is live the viewport is held at the committed target; it clears when
    /// the viewer reports the target, or after <see cref="ZoomPinLifetimeMs"/> so a later genuine scroll is never
    /// blocked.
    /// </remarks>
    private (double X, double Y, long Ticks)? _zoomPin;

    private const double ZoomPinLifetimeMs = 250;

    private enum DragKind { None, Node, Link, Pan }

    private DragKind _dragKind;
    private IWorkflowNodeViewModel? _dragNode;
    private double _dragOffsetX, _dragOffsetY;
    private (IWorkflowNodeViewModel Node, int OutputIndex)? _dragFrom;
    private Point _virtualEnd;
    private (IWorkflowNodeViewModel Node, int InputIndex)? _dropTarget;
    private Point _lastPanMouse;

    private Color _surfaceBackground = Color.FromRgb(0x1E, 0x1E, 0x1E);
    private Color _connectingLinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);

    /// <summary>Creates the surface.</summary>
    protected WorkflowTreeView()
    {
        Width = CanvasWidth;
        Height = CanvasHeight;
        Background = new SolidColorBrush(_surfaceBackground);

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUp));
        AddHandler(LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));
    }

    /// <summary>Raised after any model change, so overlays (rulers, minimap) can redraw.</summary>
    public Action? Changed;

    /// <summary>Where the cards put their ports, in design coordinates.</summary>
    public WorkflowPortLayout PortLayout { get; set; } = new();

    /// <summary>The grid and ruler renderer.</summary>
    public WorkflowGridDecorator GridDecorator { get; set; } = new();

    /// <summary>The selector the view pool uses to build node and link views.</summary>
    /// <remarks>Assign it before <see cref="SetTree"/>, or the pool has no way to build a view.</remarks>
    public IWorkflowTemplateSelector? TemplateSelector { get; set; }

    /// <summary>The surface background.</summary>
    public Color SurfaceBackground
    {
        get => _surfaceBackground;
        set
        {
            if (_surfaceBackground == value) return;
            _surfaceBackground = value;
            Background = new SolidColorBrush(value);
            InvalidateVisual();
        }
    }

    /// <summary>The stroke of the connection gesture's drag preview.</summary>
    public Color ConnectingLinkColor
    {
        get => _connectingLinkColor;
        set
        {
            if (_connectingLinkColor == value) return;
            _connectingLinkColor = value;
            InvalidateVisual();
        }
    }

    /// <summary>The bound workflow tree (for overlays like the minimap).</summary>
    public IWorkflowTreeViewModel? Tree => _tree;

    /// <summary>Visual world-origin translate: the layout's offset plus the ruler-band reserve.</summary>
    /// <remarks>
    /// Views position at <c>world + this origin</c>, and it is the physical "scroll − origin" the minimap uses.
    /// Content is inset below/right of the floating rulers so the world axes land on their inner corner, matching
    /// the other adapters.
    /// </remarks>
    public double OriginX => (_tree?.Layout.ActualOffset.Horizontal ?? 0) + WorkflowGridDecorator.RulerThickness;

    /// <summary>The vertical counterpart of <see cref="OriginX"/>.</summary>
    public double OriginY => (_tree?.Layout.ActualOffset.Vertical ?? 0) + WorkflowGridDecorator.RulerThickness;

    /// <summary>Canonical (reported) world origin — the layout offset, excluding the ruler reserve.</summary>
    /// <remarks>Feeds the info overlay and the helper, so the numbers stay identical across adapters.</remarks>
    public double ContentOriginX => _tree?.Layout.ActualOffset.Horizontal ?? 0;

    /// <summary>The vertical counterpart of <see cref="ContentOriginX"/>.</summary>
    public double ContentOriginY => _tree?.Layout.ActualOffset.Vertical ?? 0;

    /// <summary>Wires the scroll viewer whose offsets define the viewport.</summary>
    /// <param name="viewer">The viewer.</param>
    public void AttachScrollViewer(ScrollViewer viewer)
    {
        _scrollViewer = viewer;

        // 标尺带固定在视口上，所以一次滚动就要重画表面（网格 + 标尺）。SizeChanged 负责「视口尺寸变了但没滚」那种
        // 情况 —— Jalium 可能不为视口尺寸变化发 ScrollChanged。
        void OnViewportMetricsChanged()
        {
            UpdateViewport();
            InvalidateVisual();
            Changed?.Invoke();
        }

        viewer.ScrollChanged += (_, _) => OnViewportMetricsChanged();
        viewer.SizeChanged += (_, _) => OnViewportMetricsChanged();
    }

    /// <summary>Binds a tree and re-pools its visible views.</summary>
    /// <param name="tree">The tree, or <see langword="null"/> to unbind.</param>
    public void SetTree(IWorkflowTreeViewModel? tree)
    {
        _tree = tree;
        if (_tree is null)
        {
            return;
        }

        ViewPool.SetTemplateSelector(this, TemplateSelector);
        ViewPool.SetItemsSource(this, _tree.GetHelper().VisibleItems);
        if (_tree.Layout is INotifyPropertyChanged layout)
        {
            layout.PropertyChanged += OnLayoutPropertyChanged;
        }

        UpdateCanvasSize();
        UpdateViewport();
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>
    /// Re-runs viewport virtualization against the current scroll offsets.
    /// </summary>
    /// <remarks>
    /// The helper otherwise virtualizes on its ~10 fps dirty timer, so after a zoom burst the pooled views lag the
    /// freshly collapsed anchors by up to ~100 ms — deep-zoom links vanish or detach for that window. Virtualize
    /// has no equality short-circuit, so recomputing the viewport here keeps the visible set in lock-step with the
    /// committed zoom.
    /// </remarks>
    public void NotifyZoomCommitted() => NotifyZoomCommitted(
        _scrollViewer?.HorizontalOffset ?? 0,
        _scrollViewer?.VerticalOffset ?? 0);

    /// <summary>Virtualizes against a committed scroll target.</summary>
    /// <param name="hx">The committed horizontal offset.</param>
    /// <param name="vy">The committed vertical offset.</param>
    public void NotifyZoomCommitted(double hx, double vy)
    {
        if (_tree is null) return;

        // 把提交的目标钉住：Jalium 可能在落地前发一次带旧偏移的 ScrollChanged，那会覆盖掉下面要写的窗口。
        // 视口报告目标或超过 ZoomPinLifetimeMs 之后，钉子在 UpdateViewport 里释放。
        _zoomPin = (hx, vy, DateTime.UtcNow.Ticks);
        UpdateViewport(hx, vy);
        _tree.GetHelper().Virtualize(_tree.GetHelper().Viewport);
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Centers the view on a world point, growing the canvas if the target scroll runs past an edge.</summary>
    /// <param name="wx">The world X.</param>
    /// <param name="wy">The world Y.</param>
    /// <remarks>Shared by pan and the minimap's drag-to-pan.</remarks>
    public void NavigateToWorld(double wx, double wy)
    {
        if (_scrollViewer == null) return;

        double targetH = wx - _scrollViewer.ViewportWidth / 2 + OriginX;
        double targetV = wy - _scrollViewer.ViewportHeight / 2 + OriginY;
        if (targetH < 0) { GrowLeft(-targetH); targetH = 0; }
        else if (targetH > _scrollViewer.ScrollableWidth) { GrowRight(targetH - _scrollViewer.ScrollableWidth); }
        if (targetV < 0) { GrowTop(-targetV); targetV = 0; }
        else if (targetV > _scrollViewer.ScrollableHeight) { GrowBottom(targetV - _scrollViewer.ScrollableHeight); }

        _scrollViewer.ScrollToHorizontalOffset(targetH);
        _scrollViewer.ScrollToVerticalOffset(targetV);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        GridDecorator.DrawGrid(dc, OriginX, OriginY, Width, Height);
        // 节点/连线的视图是 ViewManager 在可见集上池化出来的子元素。
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext dc)
    {
        base.OnPostRender(dc);

        // 标尺带固定在视口上（绝对浮动）：画在子视图之后，才能压在它们之上；位置跟着滚动偏移走，平移时永远
        // 不离开视口。
        if (_scrollViewer is { } viewer)
        {
            GridDecorator.DrawRulers(
                dc, OriginX, OriginY,
                viewer.HorizontalOffset, viewer.VerticalOffset,
                viewer.ViewportWidth, viewer.ViewportHeight);
        }

        if (_dragKind == DragKind.Link && _dragFrom is { } from)
        {
            var center = WorkflowPortGeometry.OutputCenter(from.Node, from.OutputIndex, PortLayout);
            DrawLink(dc, new Pen(new SolidColorBrush(_connectingLinkColor), 2) { DashStyle = new DashStyle(new double[] { 4, 2 }) },
                ToCanvas(center.X, center.Y), ToCanvas(_virtualEnd.X, _virtualEnd.Y));
        }
    }

    private void DrawLink(DrawingContext dc, Pen pen, Point from, Point to)
    {
        // 与其它 GUI 一致的黄金比折线（镜像 workflow-link-view）。
        double dx = to.X - from.X;
        double stub = dx / 2.0 * (1.0 - 0.6180339887);
        var p1 = new Point(from.X + stub, from.Y);
        var p2 = new Point(to.X - stub, to.Y);
        var figure = new PathFigure { StartPoint = from, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(new[] { p1, p2, to }, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    private Point ToCanvas(double wx, double wy) => new(wx + OriginX, wy + OriginY);

    // ── 命中测试（世界坐标）────────────────────────────────────────────────

    private (IWorkflowNodeViewModel Node, int OutputIndex)? HitTestOutputPort(Point pos)
    {
        if (_tree is null) return null;

        for (int n = _tree.Nodes.Count - 1; n >= 0; n--)
        {
            var node = _tree.Nodes[n];
            var outputs = WorkflowPortGeometry.Outputs(node);
            for (int i = 0; i < outputs.Count; i++)
            {
                var c = WorkflowPortGeometry.OutputCenter(node, i, PortLayout);
                double dx = pos.X - c.X, dy = pos.Y - c.Y;
                if (dx * dx + dy * dy <= 12 * 12) return (node, i);
            }
        }

        return null;
    }

    private (IWorkflowNodeViewModel Node, int InputIndex)? HitTestInputPort(Point pos)
    {
        if (_tree is null) return null;

        for (int n = _tree.Nodes.Count - 1; n >= 0; n--)
        {
            var node = _tree.Nodes[n];
            var c = WorkflowPortGeometry.InputCenter(node, PortLayout);
            double dx = pos.X - c.X, dy = pos.Y - c.Y;
            if (dx * dx + dy * dy <= 14 * 14) return (node, 0);
        }

        return null;
    }

    private IWorkflowNodeViewModel? HitTestTitleBar(Point pos)
    {
        if (_tree is null) return null;

        for (int n = _tree.Nodes.Count - 1; n >= 0; n--)
        {
            var node = _tree.Nodes[n];
            if (pos.X >= node.Anchor.Horizontal && pos.X <= node.Anchor.Horizontal + node.Size.Width
                && pos.Y >= node.Anchor.Vertical && pos.Y <= node.Anchor.Vertical + PortLayout.TitleBarH)
                return node;
        }

        return null;
    }

    private bool HitTestCard(Point pos)
    {
        if (_tree is null) return false;

        for (int n = _tree.Nodes.Count - 1; n >= 0; n--)
        {
            var node = _tree.Nodes[n];
            if (pos.X >= node.Anchor.Horizontal && pos.X <= node.Anchor.Horizontal + node.Size.Width
                && pos.Y >= node.Anchor.Vertical && pos.Y <= node.Anchor.Vertical + node.Size.Height)
                return true;
        }

        return false;
    }

    // ── 鼠标交互（基于模型）────────────────────────────────────────────────

    private void OnMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (_tree is null || e.ChangedButton != MouseButton.Left) return;

        var pos = e.GetPosition(this);
        var world = new Point(pos.X - OriginX, pos.Y - OriginY);

        if (HitTestOutputPort(world) is { } output)
        {
            _dragKind = DragKind.Link;
            _dragFrom = output;
            _virtualEnd = world;
            _dropTarget = null;
            CaptureMouse();
            _tree.SendConnectionCommand.Execute(WorkflowPortGeometry.Outputs(output.Node)[output.OutputIndex].Slot);
            InvalidateVisual();
            Changed?.Invoke();
            e.Handled = true;
            return;
        }

        if (HitTestInputPort(world) != null) { e.Handled = true; return; }

        if (HitTestTitleBar(world) is { } node)
        {
            _dragKind = DragKind.Node;
            _dragNode = node;
            _dragOffsetX = world.X - node.Anchor.Horizontal;
            _dragOffsetY = world.Y - node.Anchor.Vertical;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (HitTestCard(world)) { e.Handled = true; return; }

        if (_scrollViewer != null)
        {
            _dragKind = DragKind.Pan;
            _lastPanMouse = e.GetPosition(_scrollViewer);
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_tree is null) return;

        switch (_dragKind)
        {
            case DragKind.Node when _dragNode != null:
            {
                var pos = e.GetPosition(this);
                var world = new Point(pos.X - OriginX, pos.Y - OriginY);
                double targetX = world.X - _dragOffsetX;
                double targetY = world.Y - _dragOffsetY;
                double dx = targetX - _dragNode.Anchor.Horizontal;
                double dy = targetY - _dragNode.Anchor.Vertical;
                if (dx != 0 || dy != 0) _dragNode.MoveCommand.Execute(new Offset(dx, dy));
                InvalidateVisual();
                Changed?.Invoke();
                e.Handled = true;
                break;
            }

            case DragKind.Link:
            {
                var pos = e.GetPosition(this);
                _virtualEnd = new Point(pos.X - OriginX, pos.Y - OriginY);
                _dropTarget = HitTestInputPort(_virtualEnd);
                _tree.SetPointerCommand.Execute(new Anchor(_virtualEnd.X, _virtualEnd.Y, 0));
                InvalidateVisual();
                Changed?.Invoke();
                e.Handled = true;
                break;
            }

            case DragKind.Pan when _scrollViewer != null:
            {
                var now = e.GetPosition(_scrollViewer);
                double dx = now.X - _lastPanMouse.X;
                double dy = now.Y - _lastPanMouse.Y;
                _lastPanMouse = now;

                // 越界延伸的规范做法（与其它表面同一策略）：按轴长度的一个**离散**增量长大
                // （DefaultPanExtendRatio），而不是每个越界帧一个像素。负向边上 helper 会返回长出的量，
                // 向前滚同样的量就在光标下抵消掉那次位移。
                double targetH = _scrollViewer.HorizontalOffset - dx;
                double targetV = _scrollViewer.VerticalOffset - dy;
                var newX = WorkflowSurfaceMath.ClampScrollOffset(
                    targetH, _scrollViewer.ScrollableWidth, _tree.Layout, horizontal: true,
                    extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
                var newY = WorkflowSurfaceMath.ClampScrollOffset(
                    targetV, _scrollViewer.ScrollableHeight, _tree.Layout, horizontal: false,
                    extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
                if (newX != targetH || newY != targetV)
                {
                    // 偏移长大了：接受新的画布范围，让视口的滚动区间跟上。
                    UpdateCanvasSize();
                    InvalidateVisual();
                    Changed?.Invoke();
                }

                _scrollViewer.ScrollToHorizontalOffset(newX);
                _scrollViewer.ScrollToVerticalOffset(newY);
                e.Handled = true;
                break;
            }
        }
    }

    private void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_tree is null || e.ChangedButton != MouseButton.Left) return;

        switch (_dragKind)
        {
            case DragKind.Node:
                _dragNode = null;
                _dragKind = DragKind.None;
                ReleaseMouseCapture();
                e.Handled = true;
                break;

            case DragKind.Link:
                if (_dropTarget is { } target && _dragFrom is { } from && target.Node != from.Node)
                {
                    var receiver = WorkflowPortGeometry.Inputs(target.Node)[target.InputIndex].Slot;
                    if (receiver is not null) _tree.ReceiveConnectionCommand.Execute(receiver);
                }
                else
                {
                    _tree.ResetVirtualLinkCommand.Execute(null);
                }

                _dragFrom = null;
                _dropTarget = null;
                _dragKind = DragKind.None;
                ReleaseMouseCapture();
                InvalidateVisual();
                Changed?.Invoke();
                e.Handled = true;
                break;

            case DragKind.Pan:
                _dragKind = DragKind.None;
                ReleaseMouseCapture();
                e.Handled = true;
                break;
        }
    }

    private void OnLostMouseCapture(object? sender, MouseEventArgs e)
    {
        if (_dragKind == DragKind.None) return;

        _dragKind = DragKind.None;
        _dragNode = null;
        _dragFrom = null;
        _dropTarget = null;
        _tree?.ResetVirtualLinkCommand.Execute(null);
        InvalidateVisual();
        Changed?.Invoke();
    }

    // ── 画布与视口 ────────────────────────────────────────────────────────

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "ActualSize" or "ActualOffset")
        {
            UpdateCanvasSize();
            UpdateViewport();
            InvalidateVisual();
            Changed?.Invoke();
        }
        else if (e.PropertyName == nameof(CanvasLayout.Scale))
        {
            // Core 的 Anchor/Size getter 按 Layout.Scale 向原点折叠，所以要重画，让自绘的卡片反映折叠后的坐标。
            InvalidateVisual();
            Changed?.Invoke();
        }
    }

    private void UpdateCanvasSize()
    {
        if (_tree is null) return;

        Width = Math.Max(CanvasWidth, _tree.Layout.ActualSize.Width);
        Height = Math.Max(CanvasHeight, _tree.Layout.ActualSize.Height);
        InvalidateMeasure();
    }

    private void UpdateViewport()
    {
        // 缩放提交之后，提交的目标在视口真正落到那里之前都是权威的：Jalium 的 ScrollTo 可能异步落地，中途
        // （或根本还没动）发的 ScrollChanged 带着旧偏移。钉住它，陈旧的写入就改不了视口窗口，也就不会剔掉刚
        // 物化出来的连线；视口报告目标（落地）或超过 ZoomPinLifetimeMs（之后真正的手势读的是实时偏移）时释放。
        if (_scrollViewer is { } pinnedViewer && _zoomPin is { } pin)
        {
            var landed = Math.Abs(pinnedViewer.HorizontalOffset - pin.X) < 0.5
                      && Math.Abs(pinnedViewer.VerticalOffset - pin.Y) < 0.5;
            var ageMs = (DateTime.UtcNow.Ticks - pin.Ticks) / TimeSpan.TicksPerMillisecond;
            if (landed || ageMs > ZoomPinLifetimeMs)
            {
                _zoomPin = null;
            }
            else
            {
                UpdateViewport(pin.X, pin.Y);
                return;
            }
        }

        UpdateViewport(_scrollViewer?.HorizontalOffset ?? 0, _scrollViewer?.VerticalOffset ?? 0);
    }

    private void UpdateViewport(double hx, double vy)
    {
        if (_tree is null) return;

        var layout = _tree.Layout;
        double vw = _scrollViewer?.ViewportWidth ?? 0;
        double vh = _scrollViewer?.ViewportHeight ?? 0;

        if (vw <= 0 || vh <= 0)
        {
            // 视口还没测量（SetTree 可能早于窗口布局，而 Jalium 的视口在初次布局时可能不发 ScrollChanged）。
            // 退回整块画布，让第一次 Virtualize 立刻物化出初始的节点/连线，而不是在 0 尺寸视口上空转、
            // 把一切都推迟到第一次真正的滚动。
            hx = layout.ActualOffset.Horizontal;
            vy = layout.ActualOffset.Vertical;
            vw = Math.Max(CanvasWidth, layout.ActualSize.Width);
            vh = Math.Max(CanvasHeight, layout.ActualSize.Height);
        }

        // 把浮动标尺带算进虚拟化，免得靠它内侧那条边的节点提前一个标尺厚度被剔除（这个自绘表面自己驱动
        // Viewport，绕过了会自动同步 inset 的那层封装）。
        _tree.SetVirtualizeInset(left: WorkflowGridDecorator.RulerThickness, top: WorkflowGridDecorator.RulerThickness);
        _tree.GetHelper().Viewport = new Viewport(
            hx - layout.ActualOffset.Horizontal,
            vy - layout.ActualOffset.Vertical,
            vw, vh);
    }

    private void GrowLeft(double amount)
    {
        if (_tree is not null) _tree.Layout.NegativeOffset += new Offset(amount, 0);
        Grow();
    }

    private void GrowRight(double amount)
    {
        if (_tree is not null) _tree.Layout.PositiveOffset += new Offset(amount, 0);
        Grow();
    }

    private void GrowTop(double amount)
    {
        if (_tree is not null) _tree.Layout.NegativeOffset += new Offset(0, amount);
        Grow();
    }

    private void GrowBottom(double amount)
    {
        if (_tree is not null) _tree.Layout.PositiveOffset += new Offset(0, amount);
        Grow();
    }

    private void Grow()
    {
        UpdateCanvasSize();
        InvalidateVisual();
        Changed?.Invoke();
    }
}
