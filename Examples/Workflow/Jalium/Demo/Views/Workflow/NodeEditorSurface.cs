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

    /// <summary>The colour every link is drawn in: what the band travels along, and what it is mixed from.</summary>
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

    /// <summary>One travelling band per settled link, keyed by the link the band belongs to.</summary>
    /// <remarks>
    /// Keyed by the link rather than by index because the link is what owns the band's identity: a link
    /// that is removed takes its band with it (<see cref="PruneFlows"/>), and one that survives a
    /// re-order keeps the band it already has instead of starting a fresh one. Entries appear on the
    /// first draw of a new link and are dropped when the tree, or the link itself, goes away.
    /// </remarks>
    private readonly Dictionary<IWorkflowLinkViewModel, LinkFlow> _flows = new();

    /// <summary>Whether the surface is on screen, and therefore whether the bands should be running.</summary>
    private bool _flowActive;

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

        // The surface paints its own links, so it is also the only thing that can own their animations.
        // Until now it had no lifetime hook at all; these two are it. Loaded is where a band may start
        // running and Unloaded is where every band stops — see the flow region below.
        Loaded += (_, _) =>
        {
            _flowActive = true;
            foreach (var flow in _flows.Values)
            {
                StartFlow(flow);
            }
        };

        Unloaded += (_, _) =>
        {
            _flowActive = false;
            StopFlows();
        };
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

        // A replaced tree took its links with it, so every band stops here rather than being left running
        // on a flow nothing will ever draw again. The new tree's links get fresh bands on the next draw,
        // and those start immediately if the surface is already on screen.
        StopFlows();

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
        // A removed link's band has nothing left to travel along, so it stops with the link instead of
        // animating an object the next draw will never reach. New links are not started here — the draw
        // that first renders them creates and starts their bands.
        PruneFlows();
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

            // Per-link brush rather than one shared pen: the band is a property of this link's own axis,
            // so it cannot live on a static. The flow is re-pointed at the ports on every draw because a
            // node drag moves them without telling the link anything.
            var flow = EnsureFlow(link);
            flow.Orient(p0, p1);

            // Two constant colours and a length of the link drawn on top of them, rather than one graded
            // stroke — see LinkFlow.Orient for why this platform gets the band that way.
            DrawLink(dc, flow.DimPen, p0, p1);
            DrawBand(dc, flow.LitPen, p0, p1, flow.BandStart, flow.BandEnd);
            DrawArrowhead(dc, flow.Arrow, p0, p1);
        }
    }

    /// <summary>
    /// The four points of a link's stub polyline, in canvas coordinates.
    /// </summary>
    /// <remarks>
    /// Golden-ratio polyline aligned with the other GUI schemes: 4 points
    /// [from, (from.X+stub, from.Y), (to.X−stub, to.Y), to] with stub = dx/2·(1−φ).
    /// <para>
    /// Shared with the band's brush rather than inlined into the draw: the gradient has to be aimed at
    /// the same four points the link is stroked along, and the box the brush is resolved against is the
    /// bounding box of exactly those points, so computing them once is what keeps the two in step.
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
    /// Strokes the lit band: the link's own polyline, clipped to the length the band covers, measured from
    /// the sender's end.
    /// </summary>
    /// <remarks>
    /// A drawn geometry rather than a gradient on the stroke, because a geometry that changes every frame is
    /// the only thing this build repaints — the measurement is on <c>LinkFlow.Orient</c>. The clipping walks
    /// the polyline's three runs by length, so the band follows an elbow instead of being projected across
    /// it, and it is what carries the cycle's phases: the band grows as it enters and shrinks as it leaves.
    /// </remarks>
    private static void DrawBand(DrawingContext dc, Pen pen, Point from, Point to, double bandStart, double bandEnd)
    {
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

    /// <summary>
    /// One link's travelling highlight: the gradient brush the link is stroked with, the solid colour its
    /// arrowhead is filled with, and the single animated number that places the band on the link.
    /// </summary>
    /// <remarks>
    /// This surface paints every link itself instead of giving each one a view, so there is no per-link
    /// object for an animation to write into — and the pens it used to draw them were <c>static
    /// readonly</c>, which one shared gradient could never have survived. This holder is that missing
    /// object: the surface keeps one per link, keyed by the link, and draws each link with the brush it
    /// carries.
    /// <para>
    /// The link is drawn dim and the band is the same colour at full strength, so what travels is a lit
    /// length of the link rather than a different colour painted onto it. The three stops are the band:
    /// the middle one carries the lit colour and the other two sit <see cref="HalfWidth"/> either side of
    /// it, which is what keeps it a band instead of one wide smear along the whole link.
    /// </para>
    /// </remarks>
    private sealed class LinkFlow
    {
        /// <summary>Half the band's width, in gradient-offset units.</summary>
        private const double HalfWidth = 0.04;

        // One cycle, as fractions of it. The phases have different lengths because they cover different
        // distances: the band travels a third of the link while forming, a third while fully lit, and a
        // third while leaving.
        private const double EnterEnd = 0.30;
        private const double FadeStart = 0.66;
        private const double BandFrom = 0.06;
        private const double BandFormed = 0.34;
        private const double BandLeaving = 0.66;
        private const double BandTo = 0.94;

        private readonly NodeEditorSurface _surface;

        public LinkFlow(NodeEditorSurface surface)
        {
            _surface = surface;

            // Transparent placeholders: the colours are known at the first Orient, which is before anything
            // is drawn with them, and are replaced rather than written into — see Orient.
            DimPen = new Pen(new SolidColorBrush(Colors.Transparent), LinkThickness);
            LitPen = new Pen(new SolidColorBrush(Colors.Transparent), LinkThickness);
            Arrow = new SolidColorBrush(Colors.Transparent);
        }

        /// <summary>The pen the link rests in: the lit colour dimmed to a little under two thirds.</summary>
        public Pen DimPen { get; private set; }

        /// <summary>
        /// The pen the band is drawn with. Replaced rather than re-coloured when the link's colour changes,
        /// because a brush the renderer has already been handed is the one thing this build does not
        /// re-read (measured; the note on <see cref="Apply"/> has the detail).
        /// </summary>
        public Pen LitPen { get; private set; }

        /// <summary>The arrowhead's fill: the band's lit colour, not the gradient.</summary>
        public SolidColorBrush Arrow { get; private set; }

        /// <summary>The link's resting colour: <see cref="Lit"/> at the link's own strength dimmed.</summary>
        public Color Dim { get; private set; }

        /// <summary>The band's colour, and the arrowhead's: the link's colour at full strength.</summary>
        public Color Lit { get; private set; }

        private double _phase;

        /// <summary>
        /// Cycle progress, 0→1: the whole of the animated state. Writing it repaints the band, and the
        /// animation writes it every frame.
        /// </summary>
        public double Phase
        {
            get => _phase;
            set
            {
                _phase = value;
                Apply();

                // The band is painted by the surface's own OnRender, not by a property the framework watches,
                // so nothing else would tell it that a stop moved. One repaint per animation frame is the
                // price of animating something the surface draws itself; the transitions tick at 60fps.
                _surface.InvalidateVisual();
            }
        }

        private Point _lastFrom;
        private Point _lastTo;
        private bool _hasAxis;

        /// <summary>
        /// Points the flow at this link: the two colours its pens carry, and the endpoints the band is
        /// measured between.
        /// </summary>
        /// <remarks>
        /// Called on every draw, because the ports it runs between move whenever a node is dragged and the
        /// link is never told. The work is skipped while the endpoints are unchanged, which is the common
        /// case, and the phase is deliberately not reset here: a drag re-points a link many times a second,
        /// and restarting the cycle on each of those would hold the band at the sender's end for the whole
        /// gesture instead of letting it run.
        /// <para>
        /// <b>Why the band is geometry and not a gradient here.</b> The other six demos stroke the link with
        /// a linear gradient and animate the middle stop; this build does not render that. A gradient handed
        /// to the renderer keeps the contents it held at hand-over: moving a stop in place, replacing the
        /// whole stop collection, moving the gradient's axis, and handing over a brand-new brush every frame
        /// all left the capture byte-identical — while a drawn geometry that changes every frame does reach
        /// the screen. So the link is drawn in its two constant colours and the band is a *length of the
        /// link* stroked on top of it, which means the animated state is where that length is and how long
        /// it is. Both of those are geometry, and geometry is what this surface repaints.
        /// </para>
        /// </remarks>
        public void Orient(Point from, Point to)
        {
            if (_hasAxis && from.X == _lastFrom.X && from.Y == _lastFrom.Y
                && to.X == _lastTo.X && to.Y == _lastTo.Y)
            {
                return;
            }

            _hasAxis = true;
            _lastFrom = from;
            _lastTo = to;

            // The two colours, and the pens that carry them. Built here rather than written into: a brush
            // this build has been handed keeps whatever it held at hand-over, so a pen is replaced whenever
            // the colour changes — in a demo with one link colour that is this first call and never again.
            Lit = LitOf(LinkColor);
            Dim = DimOf(Lit);
            DimPen = new Pen(new SolidColorBrush(Dim), LinkThickness);
            LitPen = new Pen(new SolidColorBrush(Lit), LinkThickness);
            Arrow = new SolidColorBrush(Lit);

            // The link has just been re-pointed, so the band has to be put back on it. Phase is deliberately
            // not reset here: a drag re-points a link many times a second, and restarting the cycle on each
            // of those would hold the band at the sender's end for the whole drag instead of letting it run.
            Apply();
        }

        /// <summary>
        /// Places the band and mixes its colour for the current phase — the three phases the cycle is made
        /// of, as one piecewise mapping.
        /// <para>
        /// Phase 1 (0 → <see cref="EnterEnd"/>) the band forms as it enters: it travels a third of the way
        /// while coming up from the line's resting colour to the lit one. Phase 2 (<see cref="EnterEnd"/> →
        /// <see cref="FadeStart"/>) it travels fully lit and unchanged, which is the phase that reads as
        /// flow rather than as a pulse. Phase 3 (<see cref="FadeStart"/> → 1) it leaves: the last third of
        /// the travel, settling back to the resting colour — which is also what makes the seam invisible
        /// when the cycle repeats, since the line is uniformly dim at both ends of a cycle.
        /// </para>
        /// </summary>
        private void Apply()
        {
            double centre;
            double grow;
            if (_phase < EnterEnd)
            {
                var t = _phase / EnterEnd;
                centre = BandFrom + (BandFormed - BandFrom) * t;
                grow = t;
            }
            else if (_phase < FadeStart)
            {
                var t = (_phase - EnterEnd) / (FadeStart - EnterEnd);
                centre = BandFormed + (BandLeaving - BandFormed) * t;
                grow = 1d;
            }
            else
            {
                var t = (_phase - FadeStart) / (1d - FadeStart);
                centre = BandLeaving + (BandTo - BandLeaving) * t;
                grow = 1d - t;
            }

            // Rebuilt as a whole collection rather than by moving the existing stops in place: assigning
            // GradientStops is a change to the brush itself, where a write to one of its stops is a change
            // the renderer has to be told about through the collection. On this Jalium build neither has
            // been seen to reach the cached native gradient (see the note on StartFlow), so the form kept
            // here is the one the Avalonia view's structure ports to most directly. Three stops per link
            // per frame is a small allocation beside the full-canvas repaint the change drives anyway.
            // The band, as a length of the link: where it starts and where it ends, in fractions of the
            // link's own length measured from the sender's end. The phases come out as its *size* rather
            // than as a colour: phase 1 grows it from nothing while it enters, phase 2 carries it at full
            // width, and phase 3 shrinks it away — the same "appear, travel, disappear" rhythm the other
            // six demos get from mixing the colour, in the form this surface can repaint.
            var half = HalfWidth * grow;
            BandStart = Math.Max(0d, centre - half);
            BandEnd = Math.Min(1d, centre + half);
        }

        /// <summary>Where the band begins, as a fraction of the link's length from the sender's end.</summary>
        public double BandStart { get; private set; }

        /// <summary>Where the band ends, on the same scale as <see cref="BandStart"/>.</summary>
        public double BandEnd { get; private set; }

        /// <summary>
        /// The band's colour: the link's own colour at full strength, lifted a little so a link that is
        /// already white still has somewhere brighter to go.
        /// </summary>
        private static Color LitOf(Color color)
        {
            const double lift = 0.45;

            byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

            return Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
        }

        /// <summary>
        /// The line's resting colour: the lit colour dimmed to a little under two thirds, which is what makes
        /// a lit band read as a band.
        /// </summary>
        /// <remarks>
        /// Dimming by alpha is what keeps the hue: the alternative that suggests itself — a "highlight" that
        /// is the line colour pushed <em>towards white</em> — is invisible. Jalium's links are already white,
        /// where the lifted colour and the resting one are the same pixel; on the cyan links the other demos
        /// draw, cyan lifted 75% towards white differs from cyan in one channel out of three, on a 2px line,
        /// against a dark canvas. Making the resting line the dim one puts the contrast where the eye can
        /// find it at a glance, and it works the same on both.
        /// </remarks>
        private static Color DimOf(Color color) => Color.FromArgb(
            (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);
    }

    /// <summary>
    /// Walks the band across each link once per cycle, forever, so the link reads as carrying data from the
    /// sender's anchor to the receiver's. <see cref="LinkFlow.Phase"/> is the only animated value; its
    /// setter paints the three phases.
    /// <para>
    /// Declared once and executed per link rather than built per call: the endpoint is the same every cycle,
    /// which is the case the animation reference puts in a <c>static readonly</c> field. A straight line
    /// rather than an eased curve, because the band should move at a constant speed — an ease would make
    /// each cycle pause at the ends and read as a series of pulses instead of a flow.
    /// </para>
    /// <para>
    /// The phases are one looping segment and a piecewise mapping rather than three segments joined with
    /// <c>Then()</c>, because nothing in the engine repeats a chain: a segment's <c>LoopTime</c> repeats
    /// that segment, the queue of segments is walked exactly once, and the loop guard reads a pass counter
    /// the whole run shares (<c>TransitionInterpreter.cs:194</c>) — so <c>LoopTime = int.MaxValue</c> on a
    /// first segment never reaches the second. A two-segment chain was measured reporting <c>Start</c> and
    /// <c>Completed</c> while writing zero frames, and there is no way to express "these three, in order,
    /// forever" as a chain today.
    /// </para>
    /// </summary>
    private static readonly Transition<LinkFlow> Flow =
        Transition<LinkFlow>.Create()
            .Property(t => t.Phase, 1d)
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(1.8),
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

    /// <summary>
    /// Starts one link's band from the sender's end.
    /// </summary>
    /// <remarks>
    /// The transition reads its start value from the target, so the cycle has to be at its beginning before
    /// <c>Execute</c>. The loop replays that captured start at every seam, so this is also the value each
    /// later cycle begins from.
    /// <para>
    /// <b>What this demo measured about animating a link on this build.</b> The cycle itself is sound: every
    /// link starts once, <c>Phase</c> takes roughly five hundred writes a second sweeping the whole 0→1
    /// range, and the surface's <c>OnRender</c> runs about a hundred times a second. What does not reach the
    /// screen is a change to the *gradient a link is stroked with*: stops written in place, the whole stop
    /// collection replaced, the gradient's axis moved, and a brand-new brush handed over every frame all left
    /// the capture byte-identical — the band rendered, at the right colour and width, parked at the offset a
    /// fresh cycle starts from. It is specific to the gradient's contents rather than to invalidation: writing
    /// the same brush's <c>Opacity</c> does land within a frame. A drawn geometry that changes every frame
    /// lands as well, which is why the band is carried as a length of the link instead of as a gradient on it
    /// (see <c>LinkFlow.Orient</c> and <c>DrawBand</c>).
    /// </para>
    /// <para>
    /// Carried that way it advances as intended: measured on the running demo, a lit segment of some 2–4 px
    /// reads 255 against a resting line of 169, and across six frames 220 ms apart it walks the link from
    /// about a fifth of its length to about four fifths.
    /// </para>
    /// </remarks>
    private static void StartFlow(LinkFlow flow)
    {
        flow.Phase = 0d;
        Flow.Execute(flow);
    }

    /// <summary>Stops one link's band and lets the transition release the resources it holds.</summary>
    private static void StopFlow(LinkFlow flow)
        => Transition.Exit(flow, IncludeMutual: true, IncludeNoMutual: true);

    /// <summary>
    /// This link's band, created and started the first time the link is drawn.
    /// </summary>
    /// <remarks>
    /// Starting here rather than when the link appears is what makes a link added to an already-visible tree
    /// behave the same as one the tree was built with: the draw that first reaches it is also the moment its
    /// ports are known, and running the band before then would only be animating a link with no position.
    /// </remarks>
    private LinkFlow EnsureFlow(IWorkflowLinkViewModel link)
    {
        if (_flows.TryGetValue(link, out var existing))
        {
            return existing;
        }

        var flow = new LinkFlow(this);
        _flows[link] = flow;
        if (_flowActive)
        {
            StartFlow(flow);
        }

        return flow;
    }

    /// <summary>
    /// Stops and forgets the band of every link the tree no longer holds.
    /// </summary>
    /// <remarks>
    /// Runs on a link-collection change rather than per draw, so the scan costs nothing while the tree is
    /// merely being painted. Membership is asked of the tree rather than taken from the change event because
    /// a reset carries no old items at all.
    /// </remarks>
    private void PruneFlows()
    {
        if (_flows.Count == 0)
        {
            return;
        }

        List<IWorkflowLinkViewModel>? gone = null;
        foreach (var link in _flows.Keys)
        {
            if (_tree is not null && _tree.Links.Contains(link))
            {
                continue;
            }

            (gone ??= []).Add(link);
        }

        if (gone is null)
        {
            return;
        }

        foreach (var link in gone)
        {
            StopFlow(_flows[link]);
            _flows.Remove(link);
        }
    }

    /// <summary>Stops every band and drops the state, leaving nothing animating behind a replaced or unloaded tree.</summary>
    private void StopFlows()
    {
        foreach (var flow in _flows.Values)
        {
            StopFlow(flow);
        }

        _flows.Clear();
    }

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
