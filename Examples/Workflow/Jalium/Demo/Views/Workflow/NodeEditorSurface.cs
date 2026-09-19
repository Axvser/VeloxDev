using System.Collections.Specialized;
using System.ComponentModel;
using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;
using Size = VeloxDev.WorkflowSystem.Size;

namespace Demo.Views.Workflow;

/// <summary>
/// Faithful port of the Jalium NodeEditorDemo's NodeEditorSurface (identical to the Trimmed demo),
/// bound to the VeloxDev.Core workflow model via the Common/Lib view-models. All rendering (grid,
/// links, virtual link) and interaction (drag, connect, pan, auto-grow) math is identical to the
/// trimmed demo; the only difference is the data source — generic node/slot enumeration through
/// <see cref="NodePorts"/> instead of the trimmed demo's reduced view-model shape.
/// </summary>
internal sealed class NodeEditorSurface : Canvas
{
    private const double GridStep = 40;
    private const double MajorStep = 200;
    private const double RulerThickness = 36;
    private const double Phi = 0.6180339887;
    private const double LinkThickness = 2;

    /// <summary>The colour every link is drawn in: what the band travels along, and what its two pens are built from.</summary>
    private static readonly Color LinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush s_surfaceBrush = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush s_gridMinor = new(Color.FromRgb(0x2A, 0x2D, 0x2E));
    private static readonly SolidColorBrush s_gridMajor = new(Color.FromRgb(0x3A, 0x3D, 0x40));
    private static readonly SolidColorBrush s_axisBrush = new(Color.FromRgb(0x4D, 0x4D, 0x4D));
    private static readonly SolidColorBrush s_linkBrush = new(LinkColor);
    private static readonly SolidColorBrush s_rulerBg = new(Color.FromArgb(0xC8, 0x2D, 0x2D, 0x30));
    private static readonly SolidColorBrush s_rulerLabel = new(Color.FromRgb(0xC8, 0xC8, 0xC8));
    private static readonly SolidColorBrush s_rulerTick = new(Color.FromRgb(0x6E, 0x6E, 0x6E));
    private static readonly SolidColorBrush s_rulerDivider = new(Color.FromRgb(0x4D, 0x4D, 0x4D));

    private static readonly Pen s_minorPen = new(s_gridMinor, 1);
    private static readonly Pen s_majorPen = new(s_gridMajor, 1);
    private static readonly Pen s_axisPen = new(s_axisBrush, 1.2);
    private static readonly Pen s_tickPen = new(s_rulerTick, 1);
    private static readonly Pen s_dividerPen = new(s_rulerDivider, 1);
    private static readonly Pen s_virtualPen = new(s_linkBrush, LinkThickness)
    {
        DashStyle = new DashStyle(new double[] { 4, 2 }),
    };

    /// <summary>Raised after any model/view change so overlays (minimap) can redraw.</summary>
    public Action? Changed;

    private IWorkflowTreeViewModel? _tree;
    private readonly Dictionary<IWorkflowNodeViewModel, NodeViewBase> _cards = new();
    private readonly HashSet<IWorkflowNodeViewModel> _nodeSubs = new();
    private readonly HashSet<IWorkflowSlotViewModel> _slotSubs = new();
    private ScrollViewer? _scrollViewer;

    private enum DragKind { None, Node, Link, Pan }
    private DragKind _dragKind;
    private IWorkflowNodeViewModel? _dragNode;
    private double _dragOffsetX, _dragOffsetY;
    private (IWorkflowNodeViewModel Node, int OutputIndex)? _dragFrom;
    private (IWorkflowNodeViewModel Node, int InputIndex)? _dropTarget;
    private Point _lastPanMouse;

    public NodeEditorSurface()
    {
        Width = 2000;
        Height = 2000;
        Background = s_surfaceBrush;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUp));
        AddHandler(LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnZoomMouseWheel));

        // The surface paints its own links, so it is also the only thing that can own their animation. The band
        // is one cycle over the whole surface rather than one per link, so these two are its entire lifetime:
        // Loaded is where it starts and Unloaded is where it stops — see the flow region below. (Before this
        // flow existed the surface had no lifetime hook at all; these two are it.)
        Loaded += (_, _) => StartFlow();

        Unloaded += (_, _) => StopFlow();
    }

    /// <summary>Ctrl + mouse wheel zooms the workspace: each node collapses toward the world origin
    /// by 1/scale (the Core Anchor/Size getters); the surface re-renders on Layout.Scale change.</summary>
    private void OnZoomMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (_tree is null || !e.KeyboardModifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        // Wheel up (positive delta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
        var factor = e.Delta > 0 ? 1 / 1.1 : 1.1;
        var next = System.Math.Max(0.1, System.Math.Min(10, _tree.Layout.Scale.Horizontal * factor));
        _tree.Layout.Scale = new Scale(next, next);
        e.Handled = true;
        System.Diagnostics.Debug.WriteLine($"[NodeEditorSurface] zoom wheel -> Scale {next}");
    }

    public void AttachScrollViewer(ScrollViewer viewer)
    {
        _scrollViewer = viewer;
        // The ruler bands are viewport-fixed, so a scroll must repaint the surface (grid + rulers), and
        // the virtualization window has to follow the scroll: writing helper.Viewport is what populates
        // VisibleItems (the info HUD counts them). A canvas that hosts itself instead of using the
        // adapter surface must write that viewport itself, in collapsed coordinates and after setting
        // the virtualize inset for its ruler band — the Trimmed surface does the same.
        // SizeChanged catches the viewer's first measure, which Jalium may not report as a scroll.
        void OnViewportChanged()
        {
            UpdateViewport();
            InvalidateVisual();
            Changed?.Invoke();
        }

        viewer.ScrollChanged += (_, _) => OnViewportChanged();
        viewer.SizeChanged += (_, _) => OnViewportChanged();
    }

    /// <summary>Recomputes <see cref="IWorkflowTreeHelper.Viewport"/> from the viewer's scroll offsets,
    /// in collapsed (world − ActualOffset) coordinates.</summary>
    private void UpdateViewport()
    {
        if (_tree is null)
        {
            return;
        }

        var layout = _tree.Layout;
        double hx = _scrollViewer?.HorizontalOffset ?? layout.ActualOffset.Horizontal;
        double vy = _scrollViewer?.VerticalOffset ?? layout.ActualOffset.Vertical;
        double vw = _scrollViewer?.ViewportWidth ?? 0;
        double vh = _scrollViewer?.ViewportHeight ?? 0;
        if (vw <= 0 || vh <= 0)
        {
            // The viewer isn't measured yet; fall back to the whole canvas so the first Virtualize
            // materializes immediately instead of no-op'ing on a 0-size viewport.
            hx = layout.ActualOffset.Horizontal;
            vy = layout.ActualOffset.Vertical;
            vw = Width;
            vh = Height;
        }

        // Count the ruler band into virtualization so nodes under the floating band are not culled a
        // ruler-thickness early.
        _tree.SetVirtualizeInset(left: RulerThickness, top: RulerThickness);
        _tree.GetHelper().Viewport = new Viewport(
            hx - layout.ActualOffset.Horizontal,
            vy - layout.ActualOffset.Vertical,
            vw, vh);
    }

    public void SetTree(IWorkflowTreeViewModel? tree)
    {
        UnsubscribeTree();
        _tree = tree;
        _cards.Clear();
        Children.Clear();

        // Nothing here stops the band, and this is where a surface-wide flow is simpler than a per-link one:
        // the cycle is the surface's rather than any link's or tree's, so a replaced tree does not own it and
        // the new tree's links are drawn by the cycle that is already running. Nothing has to start one either
        // — a tree is swapped on a surface that is already on screen, and Loaded is the only place the cycle
        // starts.
        if (_tree is null)
        {
            return;
        }

        _tree.Nodes.CollectionChanged += OnNodesChanged;
        _tree.Links.CollectionChanged += OnLinksChanged;
        SubscribeLayout();
        foreach (var node in _tree.Nodes)
        {
            AddCard(node);
        }

        Width = Math.Max(2000, _tree.Layout.ActualSize.Width);
        Height = Math.Max(2000, _tree.Layout.ActualSize.Height);
        // Virtualize against the current viewer (or the whole canvas before it measures), as the
        // Trimmed surface does when its tree is set.
        UpdateViewport();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void SubscribeLayout()
    {
        UnsubscribeLayout();
        if (_tree?.Layout is INotifyPropertyChanged layoutNotify)
        {
            layoutNotify.PropertyChanged += OnLayoutPropertyChanged;
        }
    }

    private void UnsubscribeLayout()
    {
        if (_tree?.Layout is INotifyPropertyChanged layoutNotify)
        {
            layoutNotify.PropertyChanged -= OnLayoutPropertyChanged;
        }
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CanvasLayout.Scale))
        {
            System.Diagnostics.Debug.WriteLine($"[NodeEditorSurface] Scale layout change -> re-position {_cards.Count} cards");
            // The Core Anchor/Size getters collapse toward the origin by Layout.Scale; re-position and
            // re-size every card box (model Anchor/Size read collapsed) and scale the card content to it
            // (ApplyScale = Width/DesignWidth), mirroring the WPF node Viewbox. Repaint links.
            foreach (var (node, card) in _cards)
            {
                card.Width = node.Size.Width;
                card.Height = node.Size.Height;
                card.ApplyScale();
                Canvas.SetLeft(card, node.Anchor.Horizontal + _tree!.Layout.ActualOffset.Horizontal);
                Canvas.SetTop(card, node.Anchor.Vertical + _tree!.Layout.ActualOffset.Vertical);
            }

            UpdateViewport();
            InvalidateVisual();
            Changed?.Invoke();
        }
        else if (e.PropertyName is "ActualSize" or "ActualOffset")
        {
            // The canvas extent and the world origin live on the layout: drag-panning past an edge and
            // the minimap's drag-to-pan grow Positive/NegativeOffset (ActualSize / ActualOffset), which
            // both moves every card (they sit at anchor + the origin) and widens the range the viewer can
            // scroll to. Adopt the grown extent (monotonic — the surface never shrinks itself, matching
            // its own Grow* paths) and re-place the cards on the new origin, so links follow their ports.
            if (_tree is not null)
            {
                Width = System.Math.Max(Width, _tree.Layout.ActualSize.Width);
                Height = System.Math.Max(Height, _tree.Layout.ActualSize.Height);
            }

            RepositionCards();
            UpdateViewport();
            InvalidateMeasure();
            InvalidateVisual();
            Changed?.Invoke();
        }
    }

    private void UnsubscribeTree()
    {
        if (_tree is null)
        {
            return;
        }

        UnsubscribeLayout();
        _tree.Nodes.CollectionChanged -= OnNodesChanged;
        _tree.Links.CollectionChanged -= OnLinksChanged;
        foreach (var node in _tree.Nodes)
        {
            UnsubscribeNode(node);
        }

        foreach (var slot in _slotSubs.ToArray())
        {
            UnsubscribeSlot(slot);
        }
    }

    // ── Geometry (world coords) ─────────────────────────────────────────────

    // Port world centers are the DESIGN local centers scaled by the collapse factor
    // (node.Size/DesignSize) — matching the card RenderTransform, so links and hit-testing land
    // exactly on the scaled port dots when the workspace zooms.
    private Point ScaledCenter(IWorkflowNodeViewModel node, Point designLocal)
    {
        _cards.TryGetValue(node, out var card);
        var sx = card is null || card.DesignWidth == 0 ? 1 : node.Size.Width / card.DesignWidth;
        var sy = card is null || card.DesignHeight == 0 ? 1 : node.Size.Height / card.DesignHeight;
        return new Point(node.Anchor.Horizontal + designLocal.X * sx, node.Anchor.Vertical + designLocal.Y * sy);
    }

    private Point InputPortCenter(IWorkflowNodeViewModel node, int inputIndex = 0)
    {
        _cards.TryGetValue(node, out var card);
        var designHeight = card?.DesignHeight ?? node.Size.Height;
        return ScaledCenter(node, NodePorts.InputCenterLocalDesign(node, inputIndex, designHeight));
    }

    private Point GetOutputPortCenter(IWorkflowNodeViewModel node, int i)
    {
        _cards.TryGetValue(node, out var card);
        var designWidth = card?.DesignWidth ?? node.Size.Width;
        var designHeight = card?.DesignHeight ?? node.Size.Height;
        return ScaledCenter(node, NodePorts.OutputCenterLocalDesign(node, i, designWidth, designHeight));
    }

    private Point GetSlotPortCenter(IWorkflowSlotViewModel slot)
    {
        var node = slot.Parent;
        if (node is null)
        {
            return default;
        }

        if (NodePorts.IndexOf(node, slot) is { } found)
        {
            return found.IsInput
                ? InputPortCenter(node, found.Index)
                : GetOutputPortCenter(node, found.Index);
        }

        return default;
    }

    private Point GetPortCenter(IWorkflowNodeViewModel node, int outputIndex)
        => GetOutputPortCenter(node, outputIndex);

    // ── Card management ─────────────────────────────────────────────────────

    private void AddCard(IWorkflowNodeViewModel node)
    {
        if (_tree is null)
        {
            return;
        }

        var card = NodeViewFactory.Create(node);
        card.Bind(node);
        card.ApplyScale();
        _cards[node] = card;
        Children.Add(card);
        Canvas.SetLeft(card, node.Anchor.Horizontal + _tree.Layout.ActualOffset.Horizontal);
        Canvas.SetTop(card, node.Anchor.Vertical + _tree.Layout.ActualOffset.Vertical);
        SubscribeNode(node);
        UpdateAllPortColors();
    }

    private void RemoveCard(IWorkflowNodeViewModel node)
    {
        if (_cards.Remove(node, out var card))
        {
            Children.Remove(card);
        }

        UnsubscribeNode(node);
    }

    private void SubscribeNode(IWorkflowNodeViewModel node)
    {
        if (_nodeSubs.Add(node) && node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnNodeChanged;
        }

        foreach (var slot in node.Slots)
        {
            SubscribeSlot(slot);
        }
    }

    private void UnsubscribeNode(IWorkflowNodeViewModel node)
    {
        if (_nodeSubs.Remove(node) && node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= OnNodeChanged;
        }
    }

    private void SubscribeSlot(IWorkflowSlotViewModel slot)
    {
        if (_slotSubs.Add(slot) && slot is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnSlotChanged;
        }
    }

    private void UnsubscribeSlot(IWorkflowSlotViewModel slot)
    {
        if (_slotSubs.Remove(slot) && slot is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= OnSlotChanged;
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is IWorkflowNodeViewModel node)
                {
                    RemoveCard(node);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is IWorkflowNodeViewModel node)
                {
                    AddCard(node);
                }
            }
        }

        InvalidateVisual();
        Changed?.Invoke();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A link that arrives or goes needs neither teardown nor start-up: the band is the surface's and it is
        // a length of whichever link is being drawn, so the next paint treats the new collection the same way
        // it treated the old one.
        InvalidateVisual();
        UpdateAllPortColors();
        Changed?.Invoke();
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_tree is null)
        {
            return;
        }

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            if (sender is IWorkflowNodeViewModel node && _cards.TryGetValue(node, out var card))
            {
                card.Width = node.Size.Width;
                card.Height = node.Size.Height;
                card.ApplyScale();
                Canvas.SetLeft(card, node.Anchor.Horizontal + _tree.Layout.ActualOffset.Horizontal);
                Canvas.SetTop(card, node.Anchor.Vertical + _tree.Layout.ActualOffset.Vertical);
            }
        }

        InvalidateVisual();
        Changed?.Invoke();
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State))
        {
            UpdateAllPortColors();
        }
    }

    // ── Auto-grow / origin (VeloxDev CanvasLayout) ──────────────────────────

    public double OriginX => _tree?.Layout.ActualOffset.Horizontal ?? 0;
    public double OriginY => _tree?.Layout.ActualOffset.Vertical ?? 0;
    public IWorkflowTreeViewModel? Tree => _tree;

    private Point ToCanvas(double wx, double wy) => new(wx + OriginX, wy + OriginY);

    /// <summary>Center the view on a world point, growing the canvas if the target scroll runs
    /// past an edge. Shared by pan and the minimap's drag-to-pan (same as the NodeEditorDemo).</summary>
    public void NavigateToWorld(double wx, double wy)
    {
        if (_scrollViewer == null)
        {
            return;
        }

        double targetH = wx - _scrollViewer.ViewportWidth / 2 + OriginX;
        double targetV = wy - _scrollViewer.ViewportHeight / 2 + OriginY;

        if (targetH < 0)
        {
            GrowLeft(-targetH);
            targetH = 0;
        }
        else if (targetH > _scrollViewer.ScrollableWidth)
        {
            GrowRight(targetH - _scrollViewer.ScrollableWidth);
        }

        if (targetV < 0)
        {
            GrowTop(-targetV);
            targetV = 0;
        }
        else if (targetV > _scrollViewer.ScrollableHeight)
        {
            GrowBottom(targetV - _scrollViewer.ScrollableHeight);
        }

        _scrollViewer.ScrollToHorizontalOffset(targetH);
        _scrollViewer.ScrollToVerticalOffset(targetV);
    }

    private void GrowLeft(double amount)
    {
        if (_tree is null) return;
        _tree.Layout.NegativeOffset += new Offset(amount, 0);
        Width += amount;
        RepositionCards();
        InvalidateMeasure();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void GrowRight(double amount)
    {
        if (_tree is null) return;
        _tree.Layout.PositiveOffset += new Offset(amount, 0);
        Width += amount;
        RepositionCards();
        InvalidateMeasure();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void GrowTop(double amount)
    {
        if (_tree is null) return;
        _tree.Layout.NegativeOffset += new Offset(0, amount);
        Height += amount;
        RepositionCards();
        InvalidateMeasure();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void GrowBottom(double amount)
    {
        if (_tree is null) return;
        _tree.Layout.PositiveOffset += new Offset(0, amount);
        Height += amount;
        RepositionCards();
        InvalidateMeasure();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void RepositionCards()
    {
        if (_tree is null)
        {
            return;
        }

        foreach (var (node, card) in _cards)
        {
            Canvas.SetLeft(card, node.Anchor.Horizontal + OriginX);
            Canvas.SetTop(card, node.Anchor.Vertical + OriginY);
        }
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); // Panel draws the dark Background
        DrawGrid(dc);
        DrawLinks(dc);
    }

    protected override void OnPostRender(DrawingContext dc)
    {
        base.OnPostRender(dc);
        // The ruler bands are viewport-fixed (absolute floating): drawn after the child views so they
        // sit on top, positioned at the scroll offset so they never leave the viewport while panning.
        DrawRulers(dc);

        if (_dragKind == DragKind.Link && _dragFrom is { } from && _tree is { VirtualLink.IsVisible: true })
        {
            var start = ToCanvas(GetPortCenter(from.Node, from.OutputIndex).X, GetPortCenter(from.Node, from.OutputIndex).Y);
            var end = ToCanvas(_tree.VirtualLink.Receiver.Anchor.Horizontal, _tree.VirtualLink.Receiver.Anchor.Vertical);
            DrawLink(dc, s_virtualPen, start, end);
        }
    }

    private void DrawGrid(DrawingContext dc)
    {
        double worldLeft = -OriginX;
        double worldRight = worldLeft + Width;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, GridStep); g <= worldRight; g += GridStep)
        {
            double x = g + OriginX;
            Pen pen = g == 0 ? s_axisPen : (Math.Abs(g % MajorStep) < 0.001 ? s_majorPen : s_minorPen);
            dc.DrawLine(pen, new Point(x, 0), new Point(x, Height));
        }

        double worldTop = -OriginY;
        double worldBottom = worldTop + Height;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, GridStep); g <= worldBottom; g += GridStep)
        {
            double y = g + OriginY;
            Pen pen = g == 0 ? s_axisPen : (Math.Abs(g % MajorStep) < 0.001 ? s_majorPen : s_minorPen);
            dc.DrawLine(pen, new Point(0, y), new Point(Width, y));
        }
    }

    private void DrawRulers(DrawingContext dc)
    {
        if (_scrollViewer is not { } viewer) return;

        const double ruler = RulerThickness;
        double originX = OriginX, originY = OriginY;
        double scrollX = viewer.HorizontalOffset, scrollY = viewer.VerticalOffset;
        double vw = viewer.ViewportWidth, vh = viewer.ViewportHeight;

        dc.DrawRectangle(s_rulerBg, null, new Rect(scrollX, scrollY, vw, ruler));
        dc.DrawRectangle(s_rulerBg, null, new Rect(scrollX, scrollY, ruler, vh));
        dc.DrawLine(s_dividerPen, new Point(scrollX + ruler, scrollY), new Point(scrollX + ruler, scrollY + vh));
        dc.DrawLine(s_dividerPen, new Point(scrollX, scrollY + ruler), new Point(scrollX + vw, scrollY + ruler));

        // Top ruler: ticks at world grid x crossing the viewport, canvas x = world + originX.
        double worldLeft = WorkflowSurfaceMath.GridWorldLeft(scrollX, originX);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, GridStep); g + originX <= scrollX + vw; g += GridStep)
        {
            double x = g + originX;
            if (x < scrollX + ruler) continue;
            bool major = Math.Abs(g % MajorStep) < 0.001;
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = Math.Abs(g) < 0.001 ? s_axisPen : s_tickPen;
            dc.DrawLine(pen, new Point(x, scrollY + ruler), new Point(x, scrollY + ruler - tick));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = s_rulerLabel };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(x + 3, scrollY + 2));
            }
        }

        // Left ruler: ticks at world grid y crossing the viewport, canvas y = world + originY.
        double worldTop = WorkflowSurfaceMath.GridWorldTop(scrollY, originY);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, GridStep); g + originY <= scrollY + vh; g += GridStep)
        {
            double y = g + originY;
            if (y < scrollY + ruler) continue;
            bool major = Math.Abs(g % MajorStep) < 0.001;
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = Math.Abs(g) < 0.001 ? s_axisPen : s_tickPen;
            dc.DrawLine(pen, new Point(scrollX + ruler, y), new Point(scrollX + ruler - tick, y));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = s_rulerLabel };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(scrollX + 3, y + 2));
            }
        }
    }

    private static string Format(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }

    private void DrawLinks(DrawingContext dc)
    {
        if (_tree is null)
        {
            return;
        }

        foreach (var link in _tree.Links)
        {
            if (!link.IsVisible)
            {
                continue;
            }

            var p0 = ToCanvas(GetSlotPortCenter(link.Sender).X, GetSlotPortCenter(link.Sender).Y);
            var p1 = ToCanvas(GetSlotPortCenter(link.Receiver).X, GetSlotPortCenter(link.Receiver).Y);

            // Two constant colours and a length of the link drawn on top of them, rather than one graded
            // stroke — see the note on the flow declaration for why this platform gets the band that way.
            // Where that length starts and ends is the surface's own state, so every link carries the same
            // band along its own axis; the ports are read here on every draw because a node drag moves them
            // without telling the link anything.
            DrawLink(dc, s_dimPen, p0, p1);
            DrawBand(dc, s_litPen, p0, p1, BandCentre, BandHalf);
            DrawArrowhead(dc, s_arrowBrush, p0, p1);
        }
    }

    /// <summary>
    /// The four points of a link's stub polyline, in canvas coordinates.
    /// </summary>
    /// <remarks>
    /// Golden-ratio polyline aligned with the other GUI schemes: 4 points
    /// [from, (from.X+stub, from.Y), (to.X−stub, to.Y), to] with stub = dx/2·(1−φ).
    /// <para>
    /// Shared with the band rather than inlined into the draw: the band is clipped along the same four points
    /// the link is stroked along, so computing them once is what keeps the two in step.
    /// </para>
    /// </remarks>
    private static Point[] LinkPoints(Point from, Point to)
    {
        double dx = to.X - from.X;
        double stub = dx / 2.0 * (1.0 - Phi);
        return
        [
            from,
            new Point(from.X + stub, from.Y),
            new Point(to.X - stub, to.Y),
            to,
        ];
    }

    private static void DrawLink(DrawingContext dc, Pen pen, Point from, Point to)
    {
        var points = LinkPoints(from, to);

        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(new[] { points[1], points[2], points[3] }, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// Strokes the lit band: the link's own polyline, clipped to the stretch of the link the band covers,
    /// measured from the sender's end.
    /// </summary>
    /// <remarks>
    /// A drawn geometry rather than a gradient on the stroke, because a geometry that changes every frame is
    /// the only thing this build repaints — the measurement is in the note on the flow declaration. The
    /// clipping walks the polyline's three runs by length, so the band follows an elbow instead of being
    /// projected across it, and the width it ends up stroked at is what carries the cycle's phases: the band
    /// grows as it enters and shrinks as it leaves.
    /// </remarks>
    private static void DrawBand(DrawingContext dc, Pen pen, Point from, Point to, double centre, double half)
    {
        // The band as the stretch the surface's two values describe: the centre plus and minus its half-width,
        // clamped to the link, so the end of a cycle stops at the link's end rather than past it.
        var bandStart = Math.Max(0d, centre - half);
        var bandEnd = Math.Min(1d, centre + half);

        if (bandEnd <= bandStart)
        {
            return;
        }

        var points = LinkPoints(from, to);
        var runs = new double[3];
        var total = 0d;
        for (var i = 0; i < 3; i++)
        {
            var dx = points[i + 1].X - points[i].X;
            var dy = points[i + 1].Y - points[i].Y;
            runs[i] = Math.Sqrt((dx * dx) + (dy * dy));
            total += runs[i];
        }

        if (total <= 0d)
        {
            return;
        }

        var start = bandStart * total;
        var end = bandEnd * total;
        var clipped = new List<Point>();
        var travelled = 0d;

        for (var i = 0; i < 3; i++)
        {
            var runStart = travelled;
            var runEnd = travelled + runs[i];
            travelled = runEnd;

            if (runEnd < start || runStart > end || runs[i] <= 0d)
            {
                continue;
            }

            var a = (Math.Max(runStart, start) - runStart) / runs[i];
            var b = (Math.Min(runEnd, end) - runStart) / runs[i];

            if (clipped.Count == 0)
            {
                clipped.Add(new Point(
                    points[i].X + ((points[i + 1].X - points[i].X) * a),
                    points[i].Y + ((points[i + 1].Y - points[i].Y) * a)));
            }

            clipped.Add(new Point(
                points[i].X + ((points[i + 1].X - points[i].X) * b),
                points[i].Y + ((points[i + 1].Y - points[i].Y) * b)));
        }

        if (clipped.Count < 2)
        {
            return;
        }

        var head = clipped[0];
        clipped.RemoveAt(0);

        var figure = new PathFigure { StartPoint = head, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(clipped.ToArray(), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawArrowhead(DrawingContext dc, Brush brush, Point from, Point to)
    {
        // Segment-aligned 12x8 arrowhead (matching WPF/WinUI/Avalonia/WinForms/MAUI).
        const double al = 12, aw = 8;
        double tx = to.X - from.X, ty = to.Y - from.Y;
        double len2 = tx * tx + ty * ty;
        if (len2 < 0.001)
        {
            return;
        }
        double len = Math.Sqrt(len2);
        tx /= len;
        ty /= len;
        double nx = -ty, ny = tx;
        double baseX = to.X - tx * al, baseY = to.Y - ty * al;

        var figure = new PathFigure { StartPoint = to, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(baseX + nx * (aw / 2), baseY + ny * (aw / 2)), true));
        figure.Segments.Add(new LineSegment(new Point(baseX - nx * (aw / 2), baseY - ny * (aw / 2)), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(brush, null, geometry);
    }

    // ── Link flow (the travelling band) ────────────────────────────────────

    /// <summary>Half the band's width, as a fraction of a link's length: the size the band travels at, and the
    /// same motion every demo's travel has, in this platform's own units.</summary>
    private const double BandHalfWidth = 0.04;

    // The three phases, as the band's centre at the end of each: it forms as it enters, travels fully lit and
    // unchanged, and shrinks away on its way out. What the animation writes is these centres, plus the width
    // where a phase is about the band's size rather than about where it is.
    private const double BandStart = 0.06;
    private const double BandFormed = 0.34;
    private const double BandLeaving = 0.66;
    private const double BandExit = 0.94;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(550);

    /// <summary>The band's colour, and the arrowhead's: the link's colour at full strength.</summary>
    private static readonly Color Lit = LitOf(LinkColor);

    /// <summary>The colour a link rests in: the lit colour dimmed to a little under two thirds.</summary>
    private static readonly Color Dim = DimOf(Lit);

    /// <summary>The two pens every link is drawn with: the one it rests in, and the one the band is stroked with.</summary>
    /// <remarks>
    /// Shared by every link, which is what they were before the band needed a gradient and what they are again
    /// now that the band is geometry: the colour they are built from is the surface's single <see cref="LinkColor"/>,
    /// so there is nothing per link left for them to hold. Built once and never written to, which is the case a
    /// brush this build has been handed survives — the note on the declaration below is the measurement.
    /// </remarks>
    private static readonly Pen s_dimPen = new(new SolidColorBrush(Dim), LinkThickness);
    private static readonly Pen s_litPen = new(new SolidColorBrush(Lit), LinkThickness);

    /// <summary>The arrowhead's fill: the colour the band travels in, not the one its line rests in.</summary>
    /// <remarks>
    /// The arrowhead is the destination marker, so it carries the band's colour rather than the link's resting
    /// one: the line rests dim, and an arrowhead dimmed with it would be the one part of the link that never
    /// read as part of the flow.
    /// </remarks>
    private static readonly SolidColorBrush s_arrowBrush = new(Lit);

    /// <summary>Whether the band's cycle is running, so that stopping it is only asked for once.</summary>
    private bool _running;

    private double _bandCentre;
    private double _bandHalf;

    /// <summary>
    /// Where the band is: the fraction of a link's length, measured from its sender's end, that the band's
    /// centre sits at. Written by the cycle every frame and read by every link as it is drawn.
    /// </summary>
    /// <remarks>
    /// A member of the surface rather than of anything per link, because this surface paints every link in one
    /// pass and there is no per-link view for an animation to write into: these two numbers are the whole of the
    /// animated state and the whole surface shares them, so every link carries its band at the same point of its
    /// own length at any moment. Writing either repaints, because the band is drawn by this surface's own
    /// <c>OnRender</c> and nothing else would tell it that the band moved — one repaint per frame is the price
    /// of animating something the surface draws itself, and the transitions tick at 60fps.
    /// </remarks>
    public double BandCentre
    {
        get => _bandCentre;
        set
        {
            _bandCentre = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Half the band's width, on the same scale as <see cref="BandCentre"/>: the rest of the animated state, and
    /// this platform's own answer to the flow's phases.
    /// </summary>
    /// <remarks>
    /// The other six demos carry a phase as a colour mix between the link's two colours; here it is the band's
    /// size, because a band on this platform is a length of the link drawn as geometry rather than a gradient on
    /// it (the note on the declaration below says why). So the cycle grows this while the band enters and takes
    /// it back to nothing while the band leaves, and a phase reads as a band appearing and disappearing rather
    /// than as the line lighting up. The band's own colour is never mixed: it is stroked lit throughout, and
    /// what changes about it is how much of the link it covers.
    /// </remarks>
    public double BandHalf
    {
        get => _bandHalf;
        set
        {
            _bandHalf = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The flow, as the three phases it is made of, declared one after the other and repeated forever.
    /// </summary>
    /// <remarks>
    /// One declaration for the whole surface rather than one per link, because the values it animates are the
    /// surface's own and every link reads the same two of them while drawing: the bands advance together, which
    /// is what a surface-wide flow looks like. That is also why this one can sit in a <c>static readonly</c>
    /// field where the Avalonia view's is built per view — every endpoint here is a constant, and a declaration
    /// that reads a local is shared by every later execution of it.
    /// <para>
    /// A straight line rather than an eased curve, because the band should move at a constant speed — an ease
    /// would make each cycle pause at the ends and read as a series of pulses instead of a flow.
    /// </para>
    /// <para>
    /// The band's width is what appears and disappears: phase 1 grows it from nothing while the band enters,
    /// phase 2 carries it at full width and unchanged, and phase 3 takes the width back to nothing as the band
    /// leaves. That is also what makes the seam invisible when the cycle repeats — the band has no width at
    /// either end of a cycle, so the centre snapping back to its captured start cannot be seen.
    /// </para>
    /// <para>
    /// <b>Why the band is geometry here and a gradient on the other six.</b> This build does not render a change
    /// to the gradient a link is stroked with: stops written in place, the whole stop collection replaced, the
    /// gradient's axis moved, and a brand-new brush handed over every frame all left the capture byte-identical —
    /// the band rendered, at the right colour and width, parked at the offset a fresh cycle starts from. It is
    /// specific to the gradient's contents rather than to invalidation: writing the same brush's <c>Opacity</c>
    /// does land within a frame, and so does a drawn geometry that changes every frame. So the link is drawn in
    /// its two constant colours and the band is a <em>length of the link</em> stroked on top of them, which
    /// makes the animated state where that length starts and how long it is — the two members above — and the
    /// drawing a clip along the link's own runs (see <see cref="DrawBand"/>).
    /// </para>
    /// <para>
    /// Carried that way it advances as intended, measured on the running demo: a lit segment of some 2–4 px
    /// reads 255 against a resting line of 169, and across six frames 220 ms apart it walks the link from about
    /// a fifth of its length to about four fifths.
    /// </para>
    /// </remarks>
    private static readonly Transition<NodeEditorSurface> Flow =
        Transition<NodeEditorSurface>.Create()
            // Phase 1 — the band forms as it enters: it travels a third of the link while coming up from no
            // width to its full one, so it appears rather than sliding in from off the link.
            .Property(s => s.BandCentre, BandFormed)
            .Property(s => s.BandHalf, BandHalfWidth)
            .Effect(new TransitionEffect()
            {
                Duration = EnterDuration,
                Ease = Eases.Default,
            })
            .Then()
            // Phase 2 — it travels fully lit and unchanged, which is the phase that reads as flow rather than
            // as a pulse: nothing about it changes except where it is.
            .Property(s => s.BandCentre, BandLeaving)
            .Effect(new TransitionEffect()
            {
                Duration = TravelDuration,
                Ease = Eases.Default,
            })
            .Then()
            // Phase 3 — it leaves, the width going back to nothing over the last third of the travel. That is
            // also what makes the seam invisible when the cycle repeats: the band has no width at either end of
            // a cycle, so the centre snapping back to its captured start cannot be seen.
            .Property(s => s.BandCentre, BandExit)
            .Property(s => s.BandHalf, 0d)
            .Effect(new TransitionEffect()
            {
                Duration = ExitDuration,
                Ease = Eases.Default,
            })
            .Repeat(int.MaxValue);

    /// <summary>
    /// Starts the cycle: the band at the sender's end of every link, with no width.
    /// </summary>
    /// <remarks>
    /// The transition reads its start values from the target, so the surface has to be at the cycle's start
    /// before <c>Execute</c> — and the loop replays that captured start at every seam, so this is also the state
    /// each later cycle begins from.
    /// </remarks>
    private void StartFlow()
    {
        BandCentre = BandStart;
        BandHalf = 0d;

        Flow.Execute(this);
        _running = true;
    }

    /// <summary>
    /// Stops the cycle, and lets the transition release the resources it holds.
    /// </summary>
    /// <remarks>
    /// A surface that leaves the tree must not leave an animation running on it — and because the cycle is the
    /// surface's own, this is the whole of the teardown: a link that comes or goes, and a tree that is replaced,
    /// have nothing here to stop.
    /// </remarks>
    private void StopFlow()
    {
        if (!_running)
        {
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    /// <summary>
    /// The band's colour: the link's own colour at full strength, lifted a little so a link that is already
    /// white still has somewhere brighter to go.
    /// </summary>
    private static Color LitOf(Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    /// <summary>
    /// The line's resting colour: the lit colour dimmed to a little under two thirds, which is what makes a lit
    /// band read as a band.
    /// </summary>
    /// <remarks>
    /// Dimming by alpha is what keeps the hue: the alternative that suggests itself — a "highlight" that is the
    /// line colour pushed <em>towards white</em> — is invisible. Jalium's links are already white, where the
    /// lifted colour and the resting one are the same pixel; on the cyan links the other demos draw, cyan lifted
    /// 75% towards white differs from cyan in one channel out of three, on a 2px line, against a dark canvas.
    /// Making the resting line the dim one puts the contrast where the eye can find it at a glance, and it works
    /// the same on both.
    /// </remarks>
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    // ── Hit testing (world coords) ─────────────────────────────────────────

    private (IWorkflowNodeViewModel Node, int OutputIndex)? HitTestOutputPort(Point pos)
    {
        if (_tree is null) return null;
        for (int n = _tree.Nodes.Count - 1; n >= 0; n--)
        {
            var node = _tree.Nodes[n];
            var outputs = NodePorts.Outputs(node);
            for (int i = 0; i < outputs.Count; i++)
            {
                var c = GetOutputPortCenter(node, i);
                double dx = pos.X - c.X, dy = pos.Y - c.Y;
                if (dx * dx + dy * dy <= 12 * 12)
                {
                    return (node, i);
                }
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
            var inputs = NodePorts.Inputs(node);
            for (int i = 0; i < inputs.Count; i++)
            {
                var c = InputPortCenter(node, i);
                double dx = pos.X - c.X, dy = pos.Y - c.Y;
                if (dx * dx + dy * dy <= 14 * 14)
                {
                    return (node, i);
                }
            }
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
                && pos.Y >= node.Anchor.Vertical && pos.Y <= node.Anchor.Vertical + NodePorts.TitleBarH)
            {
                return node;
            }
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
            {
                return true;
            }
        }

        return false;
    }

    // ── Mouse interaction ──────────────────────────────────────────────────

    private void OnMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (_tree is null || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Let interactive controls inside node cards (buttons, text boxes, check boxes) handle their
        // own press instead of starting a drag/pan.
        if (IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        var pos = e.GetPosition(this);
        var world = new Point(pos.X - OriginX, pos.Y - OriginY);

        if (HitTestOutputPort(world) is { } output)
        {
            _dragKind = DragKind.Link;
            _dragFrom = output;
            _dropTarget = null;
            CaptureMouse();
            _tree.SendConnectionCommand.Execute(NodePorts.Outputs(output.Node)[output.OutputIndex].Slot);
            UpdateAllPortColors();
            InvalidateVisual();
            Changed?.Invoke();
            e.Handled = true;
            return;
        }

        if (HitTestInputPort(world) != null)
        {
            e.Handled = true;
            return;
        }

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

        if (HitTestCard(world))
        {
            e.Handled = true;
            return;
        }

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
        if (_tree is null)
        {
            return;
        }

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
                if (dx != 0 || dy != 0)
                {
                    _dragNode.MoveCommand.Execute(new Offset(dx, dy));
                }

                if (_cards.TryGetValue(_dragNode, out var card))
                {
                    Canvas.SetLeft(card, targetX + OriginX);
                    Canvas.SetTop(card, targetY + OriginY);
                }

                InvalidateVisual();
                Changed?.Invoke();
                e.Handled = true;
                break;
            }

            case DragKind.Link:
            {
                var pos = e.GetPosition(this);
                var world = new Point(pos.X - OriginX, pos.Y - OriginY);
                _dropTarget = HitTestInputPort(world);
                _tree.SetPointerCommand.Execute(new Anchor(world.X, world.Y, 0));
                UpdateAllPortColors();
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

                if (targetH < 0)
                {
                    GrowLeft(-targetH);
                    targetH = 0;
                }
                else if (targetH > _scrollViewer.ScrollableWidth)
                {
                    GrowRight(targetH - _scrollViewer.ScrollableWidth);
                }

                if (targetV < 0)
                {
                    GrowTop(-targetV);
                    targetV = 0;
                }
                else if (targetV > _scrollViewer.ScrollableHeight)
                {
                    GrowBottom(targetV - _scrollViewer.ScrollableHeight);
                }

                _scrollViewer.ScrollToHorizontalOffset(targetH);
                _scrollViewer.ScrollToVerticalOffset(targetV);
                e.Handled = true;
                break;
            }
        }
    }

    private void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_tree is null || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

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
                    var receiver = NodePorts.Inputs(target.Node)[target.InputIndex].Slot;
                    if (receiver is not null)
                    {
                        _tree.ReceiveConnectionCommand.Execute(receiver);
                    }
                }
                else
                {
                    _tree.ResetVirtualLinkCommand.Execute(null);
                }

                _dragFrom = null;
                _dropTarget = null;
                _dragKind = DragKind.None;
                ReleaseMouseCapture();
                UpdateAllPortColors();
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
        if (_dragKind == DragKind.None)
        {
            return;
        }

        _dragKind = DragKind.None;
        _dragNode = null;
        _dragFrom = null;
        _dropTarget = null;
        _tree?.ResetVirtualLinkCommand.Execute(null);
        UpdateAllPortColors();
        InvalidateVisual();
        Changed?.Invoke();
    }

    // ── Interactive-control guard ───────────────────────────────────────────

    /// <summary>Whether the press landed on (or inside) an interactive control that should handle
    /// its own mouse input rather than the surface's drag/pan/connect.</summary>
    private static bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button or TextBox or CheckBox or ComboBox or Slider)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    // ── Port slot colors ───────────────────────────────────────────────────

    private void UpdateAllPortColors()
    {
        if (_tree is null)
        {
            return;
        }

        foreach (var (node, card) in _cards)
        {
            var outputs = NodePorts.Outputs(node);
            var outputStates = new SlotState[outputs.Count];
            for (int i = 0; i < outputStates.Length; i++)
            {
                outputStates[i] = ToState(IsSenderPort(node, i), receiver: false);
            }

            var inputs = NodePorts.Inputs(node);
            var inputStates = new SlotState[inputs.Count];
            for (int i = 0; i < inputStates.Length; i++)
            {
                inputStates[i] = ToState(sender: false, IsReceiverPort(node, i));
            }

            card.SetPortStates(inputStates, outputStates);
        }
    }

    private static SlotState ToState(bool sender, bool receiver)
    {
        var s = SlotState.StandBy;
        if (sender) s |= SlotState.Sender;
        if (receiver) s |= SlotState.Receiver;
        return s;
    }

    private bool IsSenderPort(IWorkflowNodeViewModel node, int outputIndex)
    {
        if (_tree is null)
        {
            return false;
        }

        if (_dragFrom is { } f && f.Node == node && f.OutputIndex == outputIndex)
        {
            return true;
        }

        var slot = NodePorts.Outputs(node)[outputIndex].Slot;
        if (slot is null)
        {
            return false;
        }

        foreach (var link in _tree.Links)
        {
            if (ReferenceEquals(link.Sender, slot))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsReceiverPort(IWorkflowNodeViewModel node, int inputIndex)
    {
        if (_tree is null)
        {
            return false;
        }

        if (_dropTarget is { } t && t.Node == node && t.InputIndex == inputIndex)
        {
            return true;
        }

        var input = NodePorts.Inputs(node)[inputIndex].Slot;
        if (input is null)
        {
            return false;
        }

        foreach (var link in _tree.Links)
        {
            if (ReferenceEquals(link.Receiver, input))
            {
                return true;
            }
        }

        return false;
    }
}
