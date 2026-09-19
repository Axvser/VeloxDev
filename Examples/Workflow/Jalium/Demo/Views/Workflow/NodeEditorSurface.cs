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

    // 所有链接的颜色：光带沿它行进，两支笔也由它建
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

        // 表面自己画链接，故只有它能持有链接的动画；光带是整个表面一个周期，生命周期就这两处
        // Loaded 起、Unloaded 停（见下面的 flow 区）
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
        // 标尺带视口固定，滚动必须重绘表面（网格 + 标尺）；虚拟化窗口跟着走，写 helper.Viewport 才会填 VisibleItems
        // 自己承载的 canvas 得自己写这个视口（塌缩坐标、设好标尺内缩之后，Trimmed 表面亦然）
        // SizeChanged 兜住 Jalium 可能不上报为滚动的首次测量
        void OnViewportChanged()
        {
            UpdateViewport();
            InvalidateVisual();
            Changed?.Invoke();
        }

        viewer.ScrollChanged += (_, _) => OnViewportChanged();
        viewer.SizeChanged += (_, _) => OnViewportChanged();
    }

    // 由视图器的滚动量重算 Viewport，用塌缩坐标（世界 − ActualOffset）
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
            // 视图器还没测量：退回整块画布，让首次 Virtualize 立刻有节点，而不是在零尺寸视口上空转
            hx = layout.ActualOffset.Horizontal;
            vy = layout.ActualOffset.Vertical;
            vw = Width;
            vh = Height;
        }

        // 把标尺带也算进虚拟化，浮带下面的节点才不会被提前一个标尺厚度剔除
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

        // 这里不停光带：周期是表面的而非某条链接或某棵树的，换树不影响它，新树的链接由已在跑的周期画
        // 也不用重起——树是在已上屏的表面上换的，而周期只在 Loaded 起
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
        // 按当前视图器虚拟化（测量前则按整块画布），Trimmed 表面在设树时也是这么做
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
            // 画布尺寸与世界原点都在 layout 上：拖拽平移越界、小地图拖拽都会撑大 ActualSize / ActualOffset
            // 于是接纳变大的尺寸（只增不减，与自身的 Grow* 一致），并按新原点重摆卡片，链接才跟着端口走
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
        // 链接增删不用拆也不用起：光带是表面的、是被画那条链接上的一段长度，下次绘制照旧处理新集合
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

            // 两个常量色 + 其上再画一段链接长度，而不是一条渐变描边——原因见 flow 声明处
            // 长度的起止是表面自己的状态，故每条链接沿自己的轴带同一条光带；端口每次绘制现读，拖节点不通知链接
            DrawLink(dc, s_dimPen, p0, p1);
            DrawBand(dc, s_litPen, p0, p1, BandCentre, BandHalf);
            DrawArrowhead(dc, s_arrowBrush, p0, p1);
        }
    }

    // 链接折线的四个点（画布坐标，黄金比走线）：[from, (from.X+stub, from.Y), (to.X−stub, to.Y), to]，stub = dx/2·(1−φ)
    // 与光带共用而非写进绘制里：光带按同样这四个点裁剪，算一遍两者才不会走偏
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

    // 描出光带：链接自身的折线，裁到光带覆盖的一段（自发送端量起）——实测亮段 2–4px 读 255，静息 169
    // 裁剪按长度走三段，故光带跟着拐弯而不是横跨过去；绘制用几何而非描边渐变的原因见 flow 声明
    private static void DrawBand(DrawingContext dc, Pen pen, Point from, Point to, double centre, double half)
    {
        // 光带即表面那两个数描述的一段：中心加减半宽，并夹在链接内，周期末尾停在链接端点而不是越过去
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

    // 光带半宽（占链接长度的比例）：与其它 demo 同一种运动，只是换成本平台的单位
    private const double BandHalfWidth = 0.04;

    // 三段相位各自结束时光带中心的位置：成形、全亮行进、缩小退去；动画写的就是这些中心，外加宽度
    private const double BandStart = 0.06;
    private const double BandFormed = 0.34;
    private const double BandLeaving = 0.66;
    private const double BandExit = 0.94;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(550);

    // 光带与箭头的颜色：链接本色提到全不透明
    private static readonly Color Lit = LitOf(LinkColor);

    // 链接的静息色：亮色按 alpha 变暗到约 62%
    private static readonly Color Dim = DimOf(Lit);

    // 每条链接都用这两支笔：静息的一支与描光带的一支
    // 颜色取自表面唯一的 LinkColor，无按链接的内容故共享；建好不再写——本 build 认的正是这种画刷（实测）
    private static readonly Pen s_dimPen = new(new SolidColorBrush(Dim), LinkThickness);
    private static readonly Pen s_litPen = new(new SolidColorBrush(Lit), LinkThickness);

    // 箭头填充用光带的颜色，而不是线体的静息色：否则线体暗着，箭头就成了唯一读不出流动的一段
    private static readonly SolidColorBrush s_arrowBrush = new(Lit);

    // 周期是否在跑，停只要停一次
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

    // 整个表面一条声明而非每条链接一条：写的都是表面自己的值，端点全常量故可 static readonly；匀速故不用缓动
    // 本 build 只认每帧变化的几何——渐变写停靠点/换整组/移轴/每帧新刷子截图全同（实测），故光带画成几何的一段
    private static readonly Transition<NodeEditorSurface> Flow =
        Transition<NodeEditorSurface>.Create()
            // 相位一：一边成形一边进入（走三分之一路程，宽度由零到满，是显现而非从链接外滑入）
            .Property(s => s.BandCentre, BandFormed)
            .Property(s => s.BandHalf, BandHalfWidth)
            .Effect(new TransitionEffect()
            {
                Duration = EnterDuration,
                Ease = Eases.Default,
            })
            .Then()
            // 相位二：保持全亮只移动——这一段读起来才是流动而非脉冲
            .Property(s => s.BandCentre, BandLeaving)
            .Effect(new TransitionEffect()
            {
                Duration = TravelDuration,
                Ease = Eases.Default,
            })
            .Then()
            // 相位三：一边把宽度收回零一边离开；周期两端宽度都是零，循环接缝才看不出来
            .Property(s => s.BandCentre, BandExit)
            .Property(s => s.BandHalf, 0d)
            .Effect(new TransitionEffect()
            {
                Duration = ExitDuration,
                Ease = Eases.Default,
            })
            .Repeat(int.MaxValue);

    // 起周期：光带在每条链接的发送端、宽度为零
    // 转换从目标读起值，Execute 前要先回到周期起点；循环在每个接缝重放这份抓到的起值
    private void StartFlow()
    {
        BandCentre = BandStart;
        BandHalf = 0d;

        Flow.Execute(this);
        _running = true;
    }

    // 停周期并让转换释放它持有的资源
    // 离开树的表面不能留着动画在跑；周期是表面自己的，这就是全部拆卸——链接增删、换树都不必停
    private void StopFlow()
    {
        if (!_running)
        {
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    // 亮色：链接本色各通道向白抬 45% 并置全不透明（白链接也留出更亮处）
    private static Color LitOf(Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    // 静息色：亮色按 alpha 变暗到约 62%，光带才读得出来
    // 往白里提不行：Jalium 的链接本就白，抬亮与静息同像素；青线上也只差三个通道里的一个（实测）
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
