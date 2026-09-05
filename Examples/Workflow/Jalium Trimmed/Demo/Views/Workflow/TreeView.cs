using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The interactive node-editor canvas (VeloxDev Jalium authoritative model, the Jalium NodeEditorDemo),
/// with the object-pool / virtualization mechanism of the other VeloxDev adapters: node/link views are
/// materialized by the adapter's <see cref="ViewManager"/> over the tree's <see cref="VisibleItems"/>.
/// World-coordinate model: node coordinates are FIXED world coordinates; the content is shifted by
/// Layout.ActualOffset (the world origin), growing left/up by increasing the origin, right/down by widening.
/// </summary>
public class TreeView : Canvas
{
    public const double CanvasWidth = 2000;
    public const double CanvasHeight = 2000;
    private const double Phi = 0.6180339887;

    private static readonly SolidColorBrush s_surfaceBrush = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush s_linkBrush = new(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));

    private static readonly Pen s_linkPen = new(s_linkBrush, 2);
    private static readonly Pen s_virtualPen = new(s_linkBrush, 2)
    {
        DashStyle = new DashStyle(new double[] { 4, 2 }),
    };

    /// <summary>Visual world-origin translate: the tree's layout offset PLUS the ruler-band reserve, so
    /// content is inset below/right of the floating rulers and the world axes land on the rulers' inner
    /// corner (matching WPF/Blazor). Views position at world + this origin; it is also the physical
    /// "scroll − origin" the minimap uses.</summary>
    public double OriginX => (_tree?.Layout.ActualOffset.Horizontal ?? 0) + GridDecorator.RulerThickness;
    public double OriginY => (_tree?.Layout.ActualOffset.Vertical ?? 0) + GridDecorator.RulerThickness;

    /// <summary>Canonical (reported) world origin = Layout.ActualOffset, excluding the ruler reserve —
    /// feeds the info HUD / helper so numbers stay identical across adapters.</summary>
    public double ContentOriginX => _tree?.Layout.ActualOffset.Horizontal ?? 0;
    public double ContentOriginY => _tree?.Layout.ActualOffset.Vertical ?? 0;

    /// <summary>Raised after any model change so overlays (rulers, minimap) can redraw.</summary>
    public Action? Changed;

    private IWorkflowTreeViewModel? _tree;
    private ScrollViewer? _scrollViewer;

    private enum DragKind { None, Node, Link, Pan }
    private DragKind _dragKind;
    private IWorkflowNodeViewModel? _dragNode;
    private double _dragOffsetX, _dragOffsetY;
    private (IWorkflowNodeViewModel Node, int OutputIndex)? _dragFrom;
    private Point _virtualEnd;
    private (IWorkflowNodeViewModel Node, int InputIndex)? _dropTarget;
    private Point _lastPanMouse;

    public TreeView()
    {
        Width = CanvasWidth;
        Height = CanvasHeight;
        Background = s_surfaceBrush;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUp));
        AddHandler(LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));
    }

    public void AttachScrollViewer(ScrollViewer viewer)
    {
        _scrollViewer = viewer;
        // The ruler bands are viewport-fixed, so a scroll must repaint the surface (grid + rulers).
        // SizeChanged keeps helper.Viewport (culling + info HUD) current when the viewer resizes but
        // does not scroll — Jalium may not raise ScrollChanged for a viewport-size change.
        void OnViewportMetricsChanged()
        {
            UpdateViewport();
            InvalidateVisual();
            Changed?.Invoke();
        }

        viewer.ScrollChanged += (_, _) => OnViewportMetricsChanged();
        viewer.SizeChanged += (_, _) => OnViewportMetricsChanged();
    }

    /// <summary>The bound workflow tree (for overlays like the minimap).</summary>
    public IWorkflowTreeViewModel? Tree => _tree;

    /// <summary>Template selector for the ViewPool (node → NodeView, link → LinkView); assign before <see cref="SetTree"/>.</summary>
    public IWorkflowTemplateSelector? TemplateSelector { get; set; }

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

    private void OnLayoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "ActualSize" or "ActualOffset")
        {
            UpdateCanvasSize();
            UpdateViewport();
            InvalidateVisual();
            Changed?.Invoke();
        }
        else if (e.PropertyName == nameof(VeloxDev.WorkflowSystem.CanvasLayout.Scale))
        {
            // The Core Anchor/Size getters collapse toward the origin by Layout.Scale; re-render so the
            // self-drawn node views (positioned at node.Anchor) reflect the collapsed coordinates.
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
        if (_tree is null) return;
        var layout = _tree.Layout;
        double hx = _scrollViewer?.HorizontalOffset ?? 0;
        double vy = _scrollViewer?.VerticalOffset ?? 0;
        double vw = _scrollViewer?.ViewportWidth ?? 0;
        double vh = _scrollViewer?.ViewportHeight ?? 0;
        if (vw <= 0 || vh <= 0)
        {
            // The scroll viewer isn't measured yet (SetTree can run before the window lays out, and
            // the Jalium viewer may not fire ScrollChanged on initial layout). Fall back to the whole
            // canvas so the first Virtualize materializes the initial nodes/links immediately instead
            // of no-op'ing on a 0-size viewport and deferring everything to the first real scroll.
            hx = layout.ActualOffset.Horizontal;
            vy = layout.ActualOffset.Vertical;
            vw = Math.Max(CanvasWidth, layout.ActualSize.Width);
            vh = Math.Max(CanvasHeight, layout.ActualSize.Height);
        }
        _tree.GetHelper().Viewport = new Viewport(
            hx - layout.ActualOffset.Horizontal,
            vy - layout.ActualOffset.Vertical,
            vw, vh);
    }

    /// <summary>
    /// Raised by the host after a zoom gesture is committed (Scale and the layout offsets settled) so
    /// viewport virtualization re-runs synchronously. The helper otherwise virtualizes on its ~10 fps
    /// dirty timer (TreeHelper.Update), so after a zoom burst the pooled node/link views lag the freshly
    /// collapsed anchors by up to ~100 ms — deep-zoom links vanish or detach for that window. Virtualize
    /// has no equality short-circuit (it always rebuilds; the spatial extension re-entrancy-guards
    /// itself), so recomputing the viewport here keeps VisibleItems in lock-step with the committed zoom.
    /// </summary>
    public void NotifyZoomCommitted()
    {
        if (_tree is null) return;
        UpdateViewport();
        _tree.GetHelper().Virtualize(_tree.GetHelper().Viewport);
        InvalidateVisual();
        Changed?.Invoke();
    }

    private Point ToCanvas(double wx, double wy) => new(wx + OriginX, wy + OriginY);

    // ── Port geometry (authoritative model: node.Anchor + designLocal·s) ──
    // The NodeView draws the card at the DESIGN size inside a Viewbox scaled to the collapsed box, so the
    // port world center is the DESIGN local center times the collapse factor s = node.Size/DesignSize.

    private static Point ScaledCenter(IWorkflowNodeViewModel node, Point designLocal)
    {
        var sx = SlotView.DesignWidth == 0 ? 1 : node.Size.Width / SlotView.DesignWidth;
        var sy = SlotView.DesignHeight == 0 ? 1 : node.Size.Height / SlotView.DesignHeight;
        return new Point(node.Anchor.Horizontal + designLocal.X * sx, node.Anchor.Vertical + designLocal.Y * sy);
    }

    private Point InputCenter(IWorkflowNodeViewModel node)
        => ScaledCenter(node, new Point(SlotView.InputPortX, SlotView.DesignHeight / 2.0));

    private Point OutputCenter(IWorkflowNodeViewModel node, int i)
        => ScaledCenter(node, new Point(SlotView.DesignWidth - SlotView.OutputInset, SlotView.TitleBarH + SlotView.RowH * i + SlotView.RowH / 2.0));

    // ── Auto-grow (VeloxDev model) ─────────────────────────────────────────

    private void GrowLeft(double amount)
    {
        if (_tree is not null) _tree.Layout.NegativeOffset += new Offset(amount, 0);
        UpdateCanvasSize();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void GrowRight(double amount)
    {
        if (_tree is not null) _tree.Layout.PositiveOffset += new Offset(amount, 0);
        UpdateCanvasSize();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void GrowTop(double amount)
    {
        if (_tree is not null) _tree.Layout.NegativeOffset += new Offset(0, amount);
        UpdateCanvasSize();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void GrowBottom(double amount)
    {
        if (_tree is not null) _tree.Layout.PositiveOffset += new Offset(0, amount);
        UpdateCanvasSize();
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Center the view on a world point, growing the canvas if the target scroll runs past
    /// an edge. Shared by pan and the minimap's drag-to-pan.</summary>
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

    // ── Rendering ──────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        GridDecorator.DrawGrid(dc, OriginX, OriginY, Width, Height);
        // Node/link views are pooled ViewManager children over VisibleItems.
    }

    protected override void OnPostRender(DrawingContext dc)
    {
        base.OnPostRender(dc);
        // The ruler bands are viewport-fixed (absolute floating): drawn after the child views so they
        // sit on top, positioned at the scroll offset so they never leave the viewport while panning.
        if (_scrollViewer is { } viewer)
        {
            GridDecorator.DrawRulers(dc, OriginX, OriginY,
                viewer.HorizontalOffset, viewer.VerticalOffset,
                viewer.ViewportWidth, viewer.ViewportHeight);
        }

        if (_dragKind == DragKind.Link && _dragFrom is { } from)
        {
            var start = ToCanvas(OutputCenter(from.Node, from.OutputIndex).X, OutputCenter(from.Node, from.OutputIndex).Y);
            DrawLink(dc, s_virtualPen, start, ToCanvas(_virtualEnd.X, _virtualEnd.Y));
        }
    }

    private static void DrawLink(DrawingContext dc, Pen pen, Point from, Point to)
    {
        // Golden-ratio polyline aligned with the other GUI schemes (mirrors the item template).
        double dx = to.X - from.X;
        double stub = dx / 2.0 * (1.0 - Phi);
        var p1 = new Point(from.X + stub, from.Y);
        var p2 = new Point(to.X - stub, to.Y);
        var figure = new PathFigure { StartPoint = from, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(new[] { p1, p2, to }, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    // ── Hit testing (world coords) ─────────────────────────────────────────

    private (IWorkflowNodeViewModel Node, int OutputIndex)? HitTestOutputPort(Point pos)
    {
        if (_tree is null) return null;
        for (int n = _tree.Nodes.Count - 1; n >= 0; n--)
        {
            var node = _tree.Nodes[n];
            for (int i = 0; i < SlotView.Outputs(node).Count; i++)
            {
                var c = OutputCenter(node, i);
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
            var c = InputCenter(node);
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
                && pos.Y >= node.Anchor.Vertical && pos.Y <= node.Anchor.Vertical + SlotView.TitleBarH)
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

    // ── Mouse interaction (model-based) ────────────────────────────────────

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
            _tree.SendConnectionCommand.Execute(SlotView.Outputs(output.Node)[output.OutputIndex].Slot);
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
                double targetH = _scrollViewer.HorizontalOffset - dx;
                double targetV = _scrollViewer.VerticalOffset - dy;
                if (targetH < 0) { GrowLeft(-targetH); targetH = 0; }
                else if (targetH > _scrollViewer.ScrollableWidth) { GrowRight(targetH - _scrollViewer.ScrollableWidth); }
                if (targetV < 0) { GrowTop(-targetV); targetV = 0; }
                else if (targetV > _scrollViewer.ScrollableHeight) { GrowBottom(targetV - _scrollViewer.ScrollableHeight); }
                _scrollViewer.ScrollToHorizontalOffset(targetH);
                _scrollViewer.ScrollToVerticalOffset(targetV);
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
                _dragNode = null; _dragKind = DragKind.None; ReleaseMouseCapture(); e.Handled = true; break;
            case DragKind.Link:
                if (_dropTarget is { } target && _dragFrom is { } from && target.Node != from.Node)
                {
                    var receiver = SlotView.Inputs(target.Node)[target.InputIndex].Slot;
                    if (receiver is not null) _tree.ReceiveConnectionCommand.Execute(receiver);
                }
                else _tree.ResetVirtualLinkCommand.Execute(null);
                _dragFrom = null; _dropTarget = null; _dragKind = DragKind.None;
                ReleaseMouseCapture(); InvalidateVisual(); Changed?.Invoke(); e.Handled = true;
                break;
            case DragKind.Pan:
                _dragKind = DragKind.None; ReleaseMouseCapture(); e.Handled = true; break;
        }
    }

    private void OnLostMouseCapture(object? sender, MouseEventArgs e)
    {
        if (_dragKind == DragKind.None) return;
        _dragKind = DragKind.None;
        _dragNode = null; _dragFrom = null; _dropTarget = null;
        _tree?.ResetVirtualLinkCommand.Execute(null);
        InvalidateVisual(); Changed?.Invoke();
    }
}
