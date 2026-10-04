using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Windows.Foundation;
using Windows.UI;
using WfAnchor = VeloxDev.WorkflowSystem.Anchor;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay for WinUI that renders a thumbnail overview of all nodes,
/// links, and the visible viewport in the top-right corner.
/// Uses XAML shapes (Rectangle, Line) for rendering.
/// </summary>
public class WorkflowMinimapOverlay : Canvas, IWorkflowMinimapOverlay
{
    // ── 依赖属性 ────────────────────────────────────────────────────────────

    public static readonly DependencyProperty ScrollOffsetXProperty =
        DependencyProperty.Register(nameof(ScrollOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ScrollOffsetYProperty =
        DependencyProperty.Register(nameof(ScrollOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetXProperty =
        DependencyProperty.Register(nameof(ContentOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetYProperty =
        DependencyProperty.Register(nameof(ContentOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty WorkflowTreeProperty =
        DependencyProperty.Register(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(null, (d, e) => ((WorkflowMinimapOverlay)d).OnTreeChanged((IWorkflowTreeViewModel?)e.NewValue)));

    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty IsMinimapVisibleProperty =
        DependencyProperty.Register(nameof(IsMinimapVisible), typeof(bool), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(true, OnPropChanged));

    public static readonly DependencyProperty MinimapWidthProperty =
        DependencyProperty.Register(nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(200d, OnPropChanged));

    public static readonly DependencyProperty MinimapHeightProperty =
        DependencyProperty.Register(nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(140d, OnPropChanged));

    public static readonly DependencyProperty RulerThicknessProperty =
        DependencyProperty.Register(nameof(RulerThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(28d, OnPropChanged));

    public static readonly DependencyProperty LinkStrokeThicknessProperty =
        DependencyProperty.Register(nameof(LinkStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(2.0, OnPropChanged));

    public static readonly DependencyProperty MinimapBackgroundProperty =
        DependencyProperty.Register(nameof(MinimapBackground), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(210, 20, 25, 34)), OnPropChanged));

    public static readonly DependencyProperty MinimapBorderBrushProperty =
        DependencyProperty.Register(nameof(MinimapBorderBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 148, 163, 184)), OnPropChanged));

    public static readonly DependencyProperty NodeBrushProperty =
        DependencyProperty.Register(nameof(NodeBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)), OnPropChanged));

    public static readonly DependencyProperty LinkBrushProperty =
        DependencyProperty.Register(nameof(LinkBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 180, 200, 220)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeProperty =
        DependencyProperty.Register(nameof(ViewportStroke), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportFillProperty =
        DependencyProperty.Register(nameof(ViewportFill), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeThicknessProperty =
        DependencyProperty.Register(nameof(ViewportStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.5, OnPropChanged));

    public static readonly DependencyProperty MinimapCornerRadiusProperty =
        DependencyProperty.Register(nameof(MinimapCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(4.0, OnPropChanged));

    public static readonly DependencyProperty MinimapBorderThicknessProperty =
        DependencyProperty.Register(nameof(MinimapBorderThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty NodeCornerRadiusProperty =
        DependencyProperty.Register(nameof(NodeCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty ContentPaddingProperty =
        DependencyProperty.Register(nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(2.0, OnPropChanged));

    public static readonly DependencyProperty MinimapMinSizeProperty =
        DependencyProperty.Register(nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(20.0, OnPropChanged));

    public static readonly DependencyProperty ScrollViewerNameProperty =
        DependencyProperty.Register(nameof(ScrollViewerName), typeof(string), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(null));    // ── CLR accessors ────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    /// <inheritdoc />
    public double RulerBand => 0;
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => (bool)GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }
    public double MinimapWidth { get => (double)GetValue(MinimapWidthProperty); set => SetValue(MinimapWidthProperty, value); }
    public double MinimapHeight { get => (double)GetValue(MinimapHeightProperty); set => SetValue(MinimapHeightProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public double LinkStrokeThickness { get => (double)GetValue(LinkStrokeThicknessProperty); set => SetValue(LinkStrokeThicknessProperty, value); }
    public Brush? MinimapBackground { get => (Brush?)GetValue(MinimapBackgroundProperty); set => SetValue(MinimapBackgroundProperty, value); }
    public Brush? MinimapBorderBrush { get => (Brush?)GetValue(MinimapBorderBrushProperty); set => SetValue(MinimapBorderBrushProperty, value); }
    public Brush? NodeBrush { get => (Brush?)GetValue(NodeBrushProperty); set => SetValue(NodeBrushProperty, value); }
    public Brush? LinkBrush { get => (Brush?)GetValue(LinkBrushProperty); set => SetValue(LinkBrushProperty, value); }
    public Brush? ViewportStroke { get => (Brush?)GetValue(ViewportStrokeProperty); set => SetValue(ViewportStrokeProperty, value); }
    public Brush? ViewportFill { get => (Brush?)GetValue(ViewportFillProperty); set => SetValue(ViewportFillProperty, value); }
    public double ViewportStrokeThickness { get => (double)GetValue(ViewportStrokeThicknessProperty); set => SetValue(ViewportStrokeThicknessProperty, value); }
    public double MinimapCornerRadius { get => (double)GetValue(MinimapCornerRadiusProperty); set => SetValue(MinimapCornerRadiusProperty, value); }
    public double MinimapBorderThickness { get => (double)GetValue(MinimapBorderThicknessProperty); set => SetValue(MinimapBorderThicknessProperty, value); }
    public double NodeCornerRadius { get => (double)GetValue(NodeCornerRadiusProperty); set => SetValue(NodeCornerRadiusProperty, value); }
    public double ContentPadding { get => (double)GetValue(ContentPaddingProperty); set => SetValue(ContentPaddingProperty, value); }
    public double MinimapMinSize { get => (double)GetValue(MinimapMinSizeProperty); set => SetValue(MinimapMinSizeProperty, value); }
    public string? ScrollViewerName { get => (string?)GetValue(ScrollViewerNameProperty); set => SetValue(ScrollViewerNameProperty, value); }

    // ── 状态 ────────────────────────────────────────────────────────────────

    private WorkflowBounds _lastGlobalBounds;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private WorkflowBounds _lastViewport;
    private bool _pendingRefresh = true;
    private bool _isUnloaded;
    private bool _isDragging;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private IWorkflowTreeViewModel? _subscribedTree;
    private ScrollViewer? _scrollViewer;

    // 形状池
    private readonly List<Rectangle> _nodeRects = [];
    private Rectangle? _viewportRect;
    private Rectangle? _bgRect;
    private Rectangle? _borderRect;

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _refreshTimer;

    public WorkflowMinimapOverlay()
    {
        Width = MinimapWidth;
        Height = MinimapHeight;

        // 只在 UI 线程才订阅定时器
        try
        {
            _refreshTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_refreshTimer is not null)
            {
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(16);
                _refreshTimer.Tick += (s, e) =>
                {
                    if (_isUnloaded) return;
                    try { RebuildShapes(); }
                    catch (System.Runtime.InteropServices.COMException) { /* app teardown: the DependencyObject may already be destroyed */ }
                };
            }
        }
        catch { }

        PointerPressed += OnPointerPressedHandler;
        PointerMoved += OnPointerMovedHandler;
        PointerReleased += OnPointerReleasedHandler;
        PointerCaptureLost += OnPointerCaptureLostHandler;
        Loaded += (s, e) => { _isUnloaded = false; ResolveScrollViewer(); };
        Unloaded += (_, _) => { _isUnloaded = true; _refreshTimer?.Stop(); };
    }

    private void ResolveScrollViewer()
    {
        if (string.IsNullOrWhiteSpace(ScrollViewerName)) return;
        // 上溯到 UserControl（名称作用域根）按名查找
        FrameworkElement? el = this;
        while (el is not null)
        {
            if (el is UserControl uc)
            {
                var found = uc.FindName(ScrollViewerName);
                if (found is ScrollViewer sv)
                {
                    if (_scrollViewer is not null) _scrollViewer.SizeChanged -= OnScrollViewerResized;
                    _scrollViewer = sv;
                    // 行为在 ViewChanged（滚动）时推 ViewportWidth，但视口尺寸变化不会触发 ViewChanged —— 且布局中途 ScrollViewer.ViewportWidth 会滞后。
                    // 在 ScrollViewer 自身 resize 时读它落定的视口，窗口缩小时可拖块才跟得上真实可见区。
                    _scrollViewer.SizeChanged += OnScrollViewerResized;
                }
                return;
            }
            el = VisualTreeHelper.GetParent(el) as FrameworkElement;
        }
    }

    private void OnScrollViewerResized(object? sender, SizeChangedEventArgs e)
    {
        // 延后到当前布局趟之后让尺寸落定，再推实际可见区并重渲染。ActualWidth/Height = 元素渲染尺寸（真实可见区），而 ScrollViewer.ViewportWidth 可能报有效/更大的值；窗口缩小时块必须缩到实际区域。
        // 纯 resize 不改 ScrollOffsetX/Y（块的左上角不动）。
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_scrollViewer is null) return;
            ViewportWidth = Math.Max(0, _scrollViewer.ActualWidth);
            ViewportHeight = Math.Max(0, _scrollViewer.ActualHeight);
            MarkDirty();
        });
    }

    // ── 树管理 ──────────────────────────────────────────────────────────────

    private void OnTreeChanged(IWorkflowTreeViewModel? newTree)
    {
        UnsubscribeFromTree();
        _subscribedTree = newTree;
        if (newTree is null) return;

        if (newTree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged += OnNodesChanged;
            foreach (var n in newTree.Nodes) SubscribeNode(n);
        }

        if (newTree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged += OnLinksChanged;
            foreach (var l in newTree.Links) SubscribeLink(l);
        }

        MarkDirty();
    }

    private void UnsubscribeFromTree()
    {
        if (_subscribedTree is null) return;
        if (_subscribedTree.Nodes is INotifyCollectionChanged nc) nc.CollectionChanged -= OnNodesChanged;
        foreach (var n in _subscribedNodes) if (n is INotifyPropertyChanged npc) npc.PropertyChanged -= OnNodePropChanged;
        _subscribedNodes.Clear();
        if (_subscribedTree.Links is INotifyCollectionChanged lc) lc.CollectionChanged -= OnLinksChanged;
        foreach (var l in _subscribedLinks)
        {
            if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotPropChanged;
            if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged -= OnSlotPropChanged;
        }
        _subscribedLinks.Clear();
        _subscribedTree = null;
    }

    private void SubscribeNode(IWorkflowNodeViewModel n)
    {
        if (_subscribedNodes.Add(n) && n is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnNodePropChanged;
    }

    private void SubscribeLink(IWorkflowLinkViewModel l)
    {
        if (!_subscribedLinks.Add(l)) return;
        if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged += OnSlotPropChanged;
        if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged += OnSlotPropChanged;
    }

    private void OnNodesChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (var i in e.NewItems) if (i is IWorkflowNodeViewModel n) SubscribeNode(n);
        if (e.OldItems is not null) foreach (var i in e.OldItems) if (i is IWorkflowNodeViewModel n && _subscribedNodes.Remove(n) && n is INotifyPropertyChanged npc) npc.PropertyChanged -= OnNodePropChanged;
        MarkDirty();
    }

    private void OnLinksChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (var i in e.NewItems) if (i is IWorkflowLinkViewModel l) SubscribeLink(l);
        if (e.OldItems is not null) foreach (var i in e.OldItems) if (i is IWorkflowLinkViewModel l && _subscribedLinks.Remove(l))
            {
                if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotPropChanged;
                if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged -= OnSlotPropChanged;
            }
        MarkDirty();
    }

    private void OnNodePropChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
            MarkDirty();
    }

    private void OnSlotPropChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor))
            MarkDirty();
    }

    private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WorkflowMinimapOverlay)d).MarkDirty();

    private void MarkDirty()
    {
        _pendingRefresh = true;
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        if (_refreshTimer is not null)
        {
            // 节流而非去抖：每次 MarkDirty 重启运行中的定时器会让连续平移期间它永不触发 —— 缩略图只在移动停止后重绘一次。
            // 只在空闲时启动，更新持续到来时它才按固定 16ms 节拍触发。
            if (!_refreshTimer.IsRunning)
            {
                _refreshTimer.Start();
            }
        }
        else
        {
            RebuildShapes();
        }
    }

    // ── 数据刷新 ─────────────────────────────────────────────────────────────

    private void RefreshMinimapData()
    {
        _pendingRefresh = false;
        var tree = WorkflowTree;
        if (tree is null) { ClearCache(); return; }

        var globalBounds = default(WorkflowBounds);
        _lastNodeRects.Clear();
        bool first = true;

        if (tree.Nodes is not null)
            foreach (var node in tree.Nodes)
            {
                var (nx, ny, nw, nh) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
                _lastNodeRects.Add((nx, ny, nw, nh));
                var nr = WorkflowBounds.FromNode(nx, ny, nw, nh);
                if (first) { globalBounds = nr; first = false; }
                else globalBounds = WorkflowBounds.Union(globalBounds, nr);
            }

        _lastGlobalBounds = globalBounds;

        var vw = Math.Max(1, ViewportWidth);
        var vh = Math.Max(1, ViewportHeight);
        _lastViewport = WorkflowBounds.FromNode(
            WorkflowSurfaceMath.ToWorld(ScrollOffsetX, ContentOffsetX),
            WorkflowSurfaceMath.ToWorld(ScrollOffsetY, ContentOffsetY),
            vw, vh);
    }

    private void ClearCache()
    {
        _lastNodeRects.Clear();
        _lastGlobalBounds = default;
        _lastViewport = default;
    }

    // ── 变换 ────────────────────────────────────────────────────────────────

    private (double Ox, double Oy, double MmW, double MmH, double Sc) ComputeTransform(WorkflowBounds gb)
    {
        var margin = 0.0;
        var minSz = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSz, Math.Min(MinimapWidth, ActualWidth - margin * 2));
        var mmH = Math.Max(minSz, Math.Min(MinimapHeight, ActualHeight - margin * 2));
        var pad = Math.Max(0, ContentPadding);
        var (ox, oy, sc) = WorkflowSurfaceMath.MinimapFit(gb.Width, gb.Height, mmW - pad * 2, mmH - pad * 2, pad);
        return (ox, oy, mmW, mmH, sc);
    }

    // ── 指针 ────────────────────────────────────────────────────────────────

    private Rect? GetViewportRectInMinimap()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty) return null;

        var (ox, oy, mmW, mmH, sc) = ComputeTransform(gb);
        if (sc <= 0) return null;

        // 共用拟合 + 夹取：把世界空间视口经缩略图拟合映射，并在视口超出内容时仍把块留在缩略图内。
        var (l, t, w, h) = WorkflowSurfaceMath.MinimapViewportRect(
            ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height,
            gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
        return new Rect(l, t, w, h);
    }

    private void OnPointerPressedHandler(object? sender, PointerRoutedEventArgs e)
    {
        if (_isDragging) return;
        var pt = e.GetCurrentPoint(this).Position;

        // 与 Jalium 家一致：点击点一律成为视口中心 —— 指示块上没有抓取锚点，按在哪里都重新居中。
        NavigateToWorld(pt.X, pt.Y);
        _isDragging = true;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMovedHandler(object? sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        var pt = e.GetCurrentPoint(this).Position;
        NavigateToWorld(pt.X, pt.Y);
        e.Handled = true;
    }

    private void OnPointerReleasedHandler(object? sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Handled = true;
    }

    private void OnPointerCaptureLostHandler(object? sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private void NavigateToWorld(double adjX, double adjY)
    {
        var gb = _lastGlobalBounds;
        if (gb.IsEmpty) return;
        var (ox, oy, _, _, sc) = ComputeTransform(gb);
        if (sc <= 0) return;

        var (wcx, wcy) = WorkflowSurfaceMath.MinimapToWorld(adjX, adjY, ox, oy, sc, gb.Left, gb.Top);
        var (scrollX, scrollY) = WorkflowSurfaceMath.MinimapToScroll(
            wcx, wcy, ViewportWidth, ViewportHeight, ContentOffsetX, ContentOffsetY);
        if (_scrollViewer is not null && WorkflowTree?.Layout is { } layout)
        {
            var maxH = Math.Max(0, _scrollViewer.ScrollableWidth);
            var maxV = Math.Max(0, _scrollViewer.ScrollableHeight);

            scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true);
            scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false);

            _scrollViewer.ChangeView(
                WorkflowSurfaceMath.ClampValue(scrollX, 0, maxH),
                WorkflowSurfaceMath.ClampValue(scrollY, 0, maxV),
                null, true);
        }
    }

    // ── 形状渲染 ────────────────────────────────────────────────────────────

    private void RebuildShapes()
    {
        _refreshTimer?.Stop();
        if (_isUnloaded) return;

        if (!IsMinimapVisible)
        {
            Children.Clear();
            _nodeRects.Clear();
            _viewportRect = _bgRect = _borderRect = null;
            return;
        }

        if (_pendingRefresh) RefreshMinimapData();

        var mmW = Math.Max(Math.Max(1, MinimapMinSize), Math.Min(MinimapWidth, ActualWidth));
        var mmH = Math.Max(Math.Max(1, MinimapMinSize), Math.Min(MinimapHeight, ActualHeight));

        var gb = _lastGlobalBounds;
        bool hasData = gb.Width > 0 && gb.Height > 0;

        // 确保背景/边框
        if (_bgRect is null)
        {
            _bgRect = new Rectangle();
            Children.Add(_bgRect);
        }
        _bgRect.Width = mmW;
        _bgRect.Height = mmH;
        _bgRect.Fill = MinimapBackground;
        _bgRect.RadiusX = _bgRect.RadiusY = Math.Max(0, MinimapCornerRadius);

        if (MinimapBorderBrush is not null)
        {
            if (_borderRect is null)
            {
                _borderRect = new Rectangle();
                Children.Add(_borderRect);
            }
            _borderRect.Width = mmW;
            _borderRect.Height = mmH;
            _borderRect.Stroke = MinimapBorderBrush;
            _borderRect.StrokeThickness = MinimapBorderThickness;
            _borderRect.RadiusX = _borderRect.RadiusY = Math.Max(0, MinimapCornerRadius);
        }

        if (!hasData || WorkflowTree?.Nodes is null || WorkflowTree.Nodes.Count == 0)
        {
            foreach (var r in _nodeRects) r.Visibility = Visibility.Collapsed;
            _viewportRect?.Visibility = Visibility.Collapsed;
            return;
        }

        var (ox, oy, _, _, sc) = ComputeTransform(gb);

        // 节点
        var ncr = Math.Max(0, NodeCornerRadius);
        while (_nodeRects.Count < _lastNodeRects.Count)
        {
            var rect = new Rectangle();
            _nodeRects.Add(rect);
            Children.Add(rect);
        }
        for (int i = 0; i < _nodeRects.Count; i++)
        {
            var rect = _nodeRects[i];
            if (i < _lastNodeRects.Count)
            {
                var (nx, ny, nw, nh) = _lastNodeRects[i];
                var (rx, ry) = WorkflowSurfaceMath.MinimapLocal(nx, ny, gb.Left, gb.Top, ox, oy, sc);
                var rw = WorkflowSurfaceMath.MinThumbSize(nw, sc, 2.0);
                var rh = WorkflowSurfaceMath.MinThumbSize(nh, sc, 2.0);
                SetLeft(rect, rx);
                SetTop(rect, ry);
                rect.Width = rw;
                rect.Height = rh;
                rect.Fill = NodeBrush;
                rect.RadiusX = rect.RadiusY = ncr;
                rect.Visibility = Visibility.Visible;
            }
            else
            {
                rect.Visibility = Visibility.Collapsed;
            }
        }

        // 视口指示块
        var vp = _lastViewport;
        if (!vp.IsEmpty)
        {
            if (_viewportRect is null)
            {
                _viewportRect = new Rectangle();
                Children.Add(_viewportRect);
            }
            // 与 GetViewportRectInMinimap 相同的共用拟合 + 夹取。
            var (vpx, vpy, vpw, vph) = WorkflowSurfaceMath.MinimapViewportRect(
                ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height,
                gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
            SetLeft(_viewportRect, vpx);
            SetTop(_viewportRect, vpy);
            _viewportRect.Width = vpw;
            _viewportRect.Height = vph;
            _viewportRect.Fill = ViewportFill;
            _viewportRect.Stroke = ViewportStroke;
            _viewportRect.StrokeThickness = ViewportStrokeThickness;
            _viewportRect.RadiusX = _viewportRect.RadiusY = ncr;
            _viewportRect.Visibility = Visibility.Visible;
        }
        else
        {
            _viewportRect?.Visibility = Visibility.Collapsed;
        }
    }
}
