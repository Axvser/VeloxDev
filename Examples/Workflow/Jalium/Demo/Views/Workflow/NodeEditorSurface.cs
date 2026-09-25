using System.Collections.Specialized;
using System.ComponentModel;
using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
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
    private const double LinkThickness = 2;

    // 选中（＝指针搭上）的连线：加粗 1.5，与其它六家的连线一致
    private const double SelectedLinkThickness = LinkThickness + 1.5;

    // 连线的命中半径。取值与其它六家的连线命中一致：6 个画布像素 —— 那是一个指针能稳定指到的宽度，
    // 而描边半宽（1px）要求像素级对齐，稍一挪动就落空，读起来像「看得见却抓不住」
    private const double LinkHitRadius = 6.0;

    // 所有链接的颜色。白是这套设计里链接的落点色：Avalonia 的 WorkflowView 给 BezierCurveView 传白，
    // 七家的连线本体色统一到 Avalonia 参考实现的 #CC38BDF8：彗星是「尾梢=本体色、亮头=白」，
    // 白色本体下整条彗星都是白的，色相变化就没了 —— 只剩 alpha 一层
    private static readonly Color LinkColor = Color.FromArgb(0xCC, 0x38, 0xBD, 0xF8);
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

    // 指针搭在哪颗端口上。端口是表面画的，所以悬停反馈也只能由表面自己记 ——
    // 没有端口控件可以替它答「指针在我身上吗」
    private IWorkflowSlotViewModel? _hoverSlot;

    // 当前选中的连线。链接和端口一样是表面画的，没有控件能担任这个角色；
    // 悬停即选中（与其它六家一致），Delete 与右键菜单都打在它身上
    private IWorkflowLinkViewModel? _selectedLink;

    // 右键菜单复用一份实例，同时只会开一个。它删的是「开菜单时的那条」而不是「此刻悬停的那条」：
    // 指针移进弹层去点菜单项时，悬停早已离开那条连线
    private readonly ContextMenu _linkMenu = new();
    private IWorkflowLinkViewModel? _menuTarget;

    // 菜单开着时，指针移出表面（移向弹层）不算「离开连线」—— 否则 MouseLeave 会先把选中抹掉，
    // 点下去的菜单项就没有了目标。判据直接读弹层自己的 IsOpen 而不是另立一个标志：
    // 标志一旦漏掉回落（比如 Closed 没来）就会永久卡住，悬停从此不再更新
    private bool MenuOpen => _linkMenu.IsOpen;

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

        // 表面整块就是画布，所以「把表面滚进视口」这条请求在这里吃掉：焦点一变，Jalium 的 Window 会在
        // 该元素的下一次 LayoutUpdated 上对它调 BringIntoView（Window.ScrollFocusedEditorIntoViewAfterLayout），
        // 而 2000+ 见方的画布一旦被滚进视口就是直接跳到原点 —— 用户报的「极小概率滚动」走的正是这条路。
        // 只吃目标就是表面自己的那一次：卡片里的输入框该滚进视口照常滚。
        AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnRequestBringIntoView));

        // 表面自己也处理 Delete（KeyDown），所以它保持可聚焦；但**悬停不再收焦点**了
        // （收焦点会把整块画布卷进视口，见 RequestBringIntoView 那段），主路径是 MainWindow 的窗口级预览，
        // 这一条只是第二道闸 —— 焦点在卡片里的控件上时按键会冒泡到这里
        Focusable = true;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown));
        BuildLinkMenu();

        // 表面自己画链接与端口，故只有它能持有动画；相位是整个表面一个周期，生命周期就这两处
        // Loaded 起、Unloaded 停（见下面的 flow 区）
        Loaded += (_, _) => StartFlow();

        Unloaded += (_, _) => StopFlow();

        // 指针离开表面就把悬停清掉：端口的悬停反馈是表面画的，没有人会替它发 PointerExited
        MouseLeave += (_, _) =>
        {
            if (MenuOpen)
            {
                return;
            }

            if (_hoverSlot is null && _selectedLink is null)
            {
                return;
            }

            _hoverSlot = null;
            _selectedLink = null;
            InvalidateVisual();
        };
    }

    // ── Link selection: hover highlight, the Delete key, the right-click menu ──

    // 选中色与其它六家一致（OrangeRed）。白是静息色的落点（见 LinkColor），选中就换一整套线体与彗星的颜色：
    // 只改粗细不改色的话，指针搭上一条浅蓝的线几乎看不出来
    private static readonly Color SelectedLinkColor = Colors.OrangeRed;

    /// <summary>Delete the link currently under the pointer, as a <c>Delete</c> key press does.
    /// Returns whether there was one to delete.</summary>
    public bool DeleteSelectedLink()
    {
        if (_selectedLink is null)
        {
            return false;
        }

        DeleteLink(_selectedLink);
        return true;
    }

    // 删除只有一条路：走连线自己的命令，删完把指向它的选中清掉。
    // 集合的变更通知会把绘制与弧长表带上（见 OnLinksChanged），这里只管选中这一个成员
    private void DeleteLink(IWorkflowLinkViewModel? link)
    {
        if (link is null)
        {
            return;
        }

        if (ReferenceEquals(_selectedLink, link))
        {
            _selectedLink = null;
        }

        // 菜单开着时按 Delete 也删：删完菜单不能还杵在画布上指着一条已经不在的线
        _linkMenu.IsOpen = false;
        link.DeleteCommand.Execute(null);
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DeleteSelectedLink())
        {
            e.Handled = true;
        }
    }

    // 只拦目标是自己（整块画布）的那一次滚进视口；节点卡里的控件发起的请求照旧往上冒
    private void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (ReferenceEquals(e.TargetObject, this))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 菜单只一项。这一家的连线不是控件，它没有自己的 <c>ContextMenu</c> 可挂 —— 菜单只能由画它的表面
    /// 代开，删的是开菜单那一刻记下的那条（<see cref="_menuTarget"/>）。
    /// </summary>
    private void BuildLinkMenu()
    {
        // 删的是开菜单那一刻记下的那条（_menuTarget），而不是 Closed 之后再查 —— 这一家点菜单项时
        // 先收菜单再发 Click，目标若在 Closed 里清掉，Click 拿到的就是 null
        var item = new MenuItem { Header = "删除连线" };
        item.Click += (_, _) =>
        {
            // 这一家的菜单项点完不自己收：不显式关，删掉连线之后菜单还杵在画布上挡着看得见的东西
            _linkMenu.IsOpen = false;
            DeleteLink(_menuTarget);
        };
        _linkMenu.Items.Add(item);
    }

    private void OnLinkRightClick(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (HitTestLink(pos) is not { } link)
        {
            return;
        }

        // 右键先把它选上再开菜单：菜单项删的是这条，而高亮让「删的到底是哪条」在点之前就看得见
        _selectedLink = link;
        _menuTarget = link;
        InvalidateVisual();

        // 先摆好位置再开：弹层一起来指针就算「离开」了表面，那条 MouseLeave 会连选中一起抹掉，
        // 用户看到的就是「菜单开着、线不亮」
        _linkMenu.Placement = PlacementMode.MousePoint;
        _linkMenu.PlacementTarget = this;
        _linkMenu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// 指针下最上面那条连线（画布坐标进，链接视图模型出）。逐个采样段量点到折线的距离，
    /// 量与画读的是同一张弧长表（见 <see cref="CurveFor"/>），所以只有画出来的那一道笔画能命中 ——
    /// 两端之间的空当不算。
    /// </summary>
    private IWorkflowLinkViewModel? HitTestLink(Point canvasPos)
    {
        if (_tree is null)
        {
            return null;
        }

        // 后画的压在上面，所以从集合尾部往前找
        for (int i = _tree.Links.Count - 1; i >= 0; i--)
        {
            var link = _tree.Links[i];
            if (!link.IsVisible)
            {
                continue;
            }

            var from = GetSlotPortCenter(link.Sender);
            var to = GetSlotPortCenter(link.Receiver);
            var curve = CurveFor(link, ToCanvas(from.X, from.Y), ToCanvas(to.X, to.Y));
            if (curve.DistanceTo(canvasPos) <= LinkHitRadius)
            {
                return link;
            }
        }

        return null;
    }

    /// <summary>悬停即「当前选中」，移开即取消 —— 与其它六家的连线一致。</summary>
    private void UpdateLinkHover(Point canvasPos)
    {
        // 菜单开着时指针在弹层上，那段移动不该改选中（见 MenuOpen）
        if (MenuOpen)
        {
            return;
        }

        var hovered = HitTestLink(canvasPos);
        if (ReferenceEquals(hovered, _selectedLink))
        {
            return;
        }

        _selectedLink = hovered;
        InvalidateVisual();
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
        // 换了树，上一棵树那些链接的弧长表就没人认领了（Links 变更不会通知到它们）
        _curves.Clear();
        _selectedLink = null;

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
        // 链接增删不用拆也不用起：相位是表面的，彗星是画在当次那条链接上的一段弧长，
        // 下次绘制照旧处理新集合。这里只顺手把已经不存在的链接的弧长表丢掉，表不跟着集合长
        PruneCurves();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void PruneCurves()
    {
        if (_tree is null)
        {
            return;
        }

        var alive = new HashSet<IWorkflowLinkViewModel>(_tree.Links);

        // 选中的那条也可能已经不在了：删除不止表面这一条路（Agent、Undo 都会删连线）。
        // 留着一个不在树上的选中，下一次 Delete 就打在空气上
        if (_selectedLink is not null && !alive.Contains(_selectedLink))
        {
            _selectedLink = null;
        }

        // 菜单记着的那条同理：它已经不在了，菜单项再点也不该去动一条不在树上的线
        if (_menuTarget is not null && !alive.Contains(_menuTarget))
        {
            _menuTarget = null;
        }

        foreach (var link in _curves.Keys.ToArray())
        {
            if (!alive.Contains(link))
            {
                _curves.Remove(link);
            }
        }
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
        // 端口的状态颜色现在每次绘制现算，所以这两个属性只需催一次重绘（Channel 决定波纹朝哪边走）
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State) or nameof(IWorkflowSlotViewModel.Channel))
        {
            InvalidateVisual();
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

        // 端口排在卡片之后画：卡片是表面的子元素，OnPostRender 整段在它们之后，
        // 于是「端口压过卡面」是白拿的，不必再问 ZIndex 在这台机子上认不认（Avalonia 那边要靠 ZIndex=6）
        DrawPorts(dc);

        if (_dragKind == DragKind.Link && _dragFrom is { } from && _tree is { VirtualLink.IsVisible: true })
        {
            var start = ToCanvas(GetPortCenter(from.Node, from.OutputIndex).X, GetPortCenter(from.Node, from.OutputIndex).Y);
            var end = ToCanvas(_tree.VirtualLink.Receiver.Anchor.Horizontal, _tree.VirtualLink.Receiver.Anchor.Vertical);
            _virtualCurve.Ensure(start, end);
            dc.DrawGeometry(null, s_virtualPen, _virtualCurve.Segment(0, _virtualCurve.Length));
        }

        // 标尺带视口固定（绝对浮贴），排在最后：滚动时它永远贴着视口，也永远在最上面
        DrawRulers(dc);
    }

    // ── Ports (the ring + its ripples) ─────────────────────────────────────

    /// <summary>
    /// 画一个节点上的每颗端口：字形（环 + 芯）与它的端口名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么端口在表面上画，而不是像 Avalonia 那样放进卡片自己：这一家的渲染器按<b>布局盒</b>裁剪子元素
    /// （<c>Visual.ShouldRenderChild</c> 只看布局盒、不看画出来的内容，见 Jalium Trimmed 那个 LinkView 的注释）——
    /// 端口有一半骑在卡边外，放进卡里就等于把外溢的那一半交给一个刚好到此为止的盒子去决定；
    /// 而 Enum 卡根上那个裁到圆角的主体区（<c>NodeChrome</c> 给卡片设了 <c>ClipToBounds</c>）会直接切掉它。
    /// 表面自己的盒子覆盖整个视口，端口因此永远不越界，也不用像 Avalonia 那样逐卡去挪 ScrollViewer 的视口。
    /// </para>
    /// <para>
    /// 位置仍是 <see cref="NodePorts"/> 那<b>一处</b>给的（连线端点与命中测试读的也是它），
    /// 所以字形、名字、连线三者不可能对不齐。
    /// </para>
    /// </remarks>
    private void DrawPorts(DrawingContext dc)
    {
        if (_tree is null)
        {
            return;
        }

        double phase = FlowPhase;

        foreach (var (node, card) in _cards)
        {
            if (!NearViewport(node, card))
            {
                continue;
            }

            // 设计坐标 → 画布：卡片被 Viewbox 按 node.Size/设计尺寸 缩放，端口的字形跟着同一比例走
            double scale = PortScale(node, card);
            double box = NodePorts.PortBoxSize(node) * scale;
            if (box <= 2)
            {
                continue;
            }

            var inputs = NodePorts.Inputs(node);
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Slot is not { } slot)
                {
                    continue;
                }

                var local = InputPortCenter(node, i);
                var center = ToCanvas(local.X, local.Y);
                PortGlyph.Draw(dc, center, box, InputPortState(node, i),
                    slot.Channel, phase, ReferenceEquals(_hoverSlot, slot));

                if (inputs[i].Name.Length > 0)
                {
                    DrawSlotName(dc, inputs[i].Name, center, scale, NodePorts.SlotNameInset, input: true, node);
                }
            }

            var outputs = NodePorts.Outputs(node);
            for (int i = 0; i < outputs.Count; i++)
            {
                if (outputs[i].Slot is not { } slot)
                {
                    continue;
                }

                var local = GetOutputPortCenter(node, i);
                var center = ToCanvas(local.X, local.Y);
                PortGlyph.Draw(dc, center, box, OutputPortState(node, i),
                    slot.Channel, phase, ReferenceEquals(_hoverSlot, slot));

                if (outputs[i].Name.Length > 0)
                {
                    DrawSlotName(dc, outputs[i].Name, center, scale, NodePorts.SlotNameInset, input: false, node);
                }
            }
        }
    }

    /// <summary>端口名：行内那颗小字，居中在它那口的行上，往卡里缩 <paramref name="inset"/> 设计单位。</summary>
    private static void DrawSlotName(DrawingContext dc, string name, Point center, double scale, double inset,
        bool input, IWorkflowNodeViewModel node)
    {
        var text = new FormattedText(name, "Segoe UI", CardPalette.SlotNameSize * scale)
        {
            Foreground = AccentBrushOf(node),
            FontWeight = FontWeights.SemiBold.ToOpenTypeWeight(),
        };
        TextMeasurement.MeasureText(text);

        double x = input ? center.X + (inset * scale) : center.X - (inset * scale) - text.Width;
        dc.DrawText(text, new Point(x, center.Y - (text.Height / 2)));
    }

    // 端口名取节点自己的类型色（Python/Timer 的天蓝、Enum 的紫），与标题行那条色条同一个来源。
    // 画刷建好留着：这份映射只有四种结果，而端口名是每帧重画的
    private static readonly SolidColorBrush s_slotNameTimerPython = new(CardPalette.AccentTimerPython);
    private static readonly SolidColorBrush s_slotNameEnum = new(CardPalette.AccentEnum);
    private static readonly SolidColorBrush s_slotNameController = new(CardPalette.AccentController);
    private static readonly SolidColorBrush s_slotNameFallback = new(CardPalette.AccentFallback);

    private static Brush AccentBrushOf(IWorkflowNodeViewModel node) => node switch
    {
        PythonScriptNodeViewModel or TimerNodeViewModel => s_slotNameTimerPython,
        EnumSelectorNodeViewModel => s_slotNameEnum,
        ControllerViewModel => s_slotNameController,
        _ => s_slotNameFallback,
    };

    /// <summary>卡片当前被缩放的比例：Viewbox 是等比缩放的，取两轴的较小者才与它一致。</summary>
    private static double PortScale(IWorkflowNodeViewModel node, NodeViewBase card)
    {
        double sx = card.DesignWidth <= 0 ? 1 : node.Size.Width / card.DesignWidth;
        double sy = card.DesignHeight <= 0 ? 1 : node.Size.Height / card.DesignHeight;
        return Math.Min(sx, sy);
    }

    /// <summary>
    /// 视口粗筛。端口画在表面自己的坐标里，不受「子元素布局盒」那套剔除影响，但逐个画整棵树的端口在
    /// 缩到很远时是白费力气 —— 卡片在视口之外就整块跳过（留一点余量给外溢的那半个口）。
    /// </summary>
    private bool NearViewport(IWorkflowNodeViewModel node, NodeViewBase card)
    {
        if (_scrollViewer is not { } viewer)
        {
            return true;
        }

        double pad = (NodePorts.PortBoxSize(node) / 2) + 16;
        double x0 = node.Anchor.Horizontal + OriginX;
        double y0 = node.Anchor.Vertical + OriginY;
        double vx = viewer.HorizontalOffset, vy = viewer.VerticalOffset;

        return x0 + node.Size.Width + pad >= vx
            && x0 - pad <= vx + viewer.ViewportWidth
            && y0 + node.Size.Height + pad >= vy
            && y0 - pad <= vy + viewer.ViewportHeight;
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

            var from = GetSlotPortCenter(link.Sender);
            var to = GetSlotPortCenter(link.Receiver);
            var p0 = ToCanvas(from.X, from.Y);
            var p1 = ToCanvas(to.X, to.Y);

            // 视口之外的链接整条跳过。这一条在这里比在别处更要紧：彗星每帧要按弧长切出十几段几何
            // （尾段 × 两遍），是整块绘制里最贵的一处，而它只在画得到的地方才有意义
            if (!CrossesViewport(p0, p1))
            {
                continue;
            }

            // 线体是静息的，光不在时它只是一根暗线 —— 有了对比，沿它跑的那段彗星才亮得出来。
            // 端点每次绘制现读：拖节点只动 Anchor，链接本身收不到通知，所以几何按端点缓存（见 LinkCurve）
            var curve = CurveFor(link, p0, p1);
            if (curve.Length <= 0)
            {
                continue;
            }

            // 选中（＝指针搭上）的那条整条换色加粗，彗星跟着走：它跑在这条线上，亮着红尾巴的蓝线读起来像两条线
            bool selected = ReferenceEquals(link, _selectedLink);
            var color = selected ? SelectedLinkColor : LinkColor;
            double thickness = selected ? SelectedLinkThickness : LinkThickness;

            DrawLink(dc, curve, color, thickness);
            DrawComet(dc, curve, color, thickness);
        }
    }

    /// <summary>
    /// 一条链接是否可能出现在视口里。两点都落在视口外也照样可能穿过视口（贝塞尔是弯的），
    /// 所以判的是<b>两点连线</b>的包围盒与视口相交，再往外留一段余量把控制点拉出去的弧度也算进去。
    /// </summary>
    private bool CrossesViewport(Point a, Point b)
    {
        if (_scrollViewer is not { } viewer)
        {
            return true;
        }

        // 控制点水平拉出 max(40, |dx|·0.5)，所以曲线最多比两端连线多探出约 |dx| 的一半
        double pad = Math.Max(40, Math.Abs(b.X - a.X) * 0.5);
        double vx = viewer.HorizontalOffset, vy = viewer.VerticalOffset;
        double vr = vx + viewer.ViewportWidth, vb = vy + viewer.ViewportHeight;

        return Math.Max(a.X, b.X) + pad >= vx
            && Math.Min(a.X, b.X) - pad <= vr
            && Math.Max(a.Y, b.Y) >= vy
            && Math.Min(a.Y, b.Y) <= vb;
    }

    // ── 链接几何（三次贝塞尔 + 弧长表） ────────────────────────────────────

    /// <summary>
    /// 一条链接的弧长表：<see cref="SampleCount"/> 段的采样点 + 累计长度 + 全长。
    /// <para>
    /// 为什么要存表而不是每帧现算：彗星是按<b>弧长</b>切的（头与尾各取一段长度），而贝塞尔的 t 与弧长
    /// 并不成正比 —— 现算的话光在弯道上会忽快忽慢，正是这个 demo 换掉折线时想修掉的观感。表只在端点
    /// 变化时重建，判据就是建表时的那两个端点。
    /// </para>
    /// <para>
    /// 用 <see cref="PathGeometry"/> + <see cref="PolyLineSegment"/> 而不是 <c>StreamGeometry</c>：
    /// 后者的上下文接口（BeginFigure / LineTo 的重载）在本平台上没有文档，前者是这个仓库里已经在跑的组合。
    /// </para>
    /// </summary>
    private sealed class LinkCurve
    {
        private readonly Point[] _samples = new Point[SampleCount + 1];
        private readonly double[] _cumulative = new double[SampleCount + 1];
        private Point _from;
        private Point _to;
        private double _length;

        public double Length => _length;

        /// <summary>端点变了就重建弧长表，否则原样留着。返回是否重建过。</summary>
        public bool Ensure(Point from, Point to)
        {
            if (_length > 0
                && from.X == _from.X && from.Y == _from.Y
                && to.X == _to.X && to.Y == _to.Y)
            {
                return false;
            }

            _from = from;
            _to = to;
            for (int i = 0; i <= SampleCount; i++)
            {
                _samples[i] = At(i / (double)SampleCount);
            }

            _cumulative[0] = 0;
            for (int i = 1; i <= SampleCount; i++)
            {
                double dx = _samples[i].X - _samples[i - 1].X;
                double dy = _samples[i].Y - _samples[i - 1].Y;
                _cumulative[i] = _cumulative[i - 1] + Math.Sqrt((dx * dx) + (dy * dy));
            }

            _length = _cumulative[SampleCount];
            return true;
        }

        // 两个控制点各自水平拉开：连线因此从两端水平出线、中间平滑过渡，没有折角。
        // 最小拉出量那条不是装饰：两个端口靠得很近时 0.5·dx 会让曲线退化成一条直线段，
        // 失去「从端口水平出来」的形状
        private (Point C1, Point C2) Controls()
        {
            double dx = _to.X - _from.X;
            double pull = Math.Max(40, Math.Abs(dx) * 0.5);
            return (new Point(_from.X + pull, _from.Y), new Point(_to.X - pull, _to.Y));
        }

        private Point At(double t)
        {
            var (c1, c2) = Controls();
            double u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;

            return new Point(
                (a * _from.X) + (b * c1.X) + (c * c2.X) + (d * _to.X),
                (a * _from.Y) + (b * c1.Y) + (c * c2.Y) + (d * _to.Y));
        }

        /// <summary>弧长 → 点。二分找所在采样段再线性插值，所以取点精确到亚像素，不受采样密度限制。</summary>
        public Point AtLength(double len)
        {
            if (_length <= 0)
            {
                return _from;
            }

            len = Math.Clamp(len, 0, _length);

            int lo = 0, hi = _cumulative.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] <= len)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            double span = _cumulative[hi] - _cumulative[lo];
            double t = span <= 0 ? 0 : (len - _cumulative[lo]) / span;

            return new Point(
                _samples[lo].X + ((_samples[hi].X - _samples[lo].X) * t),
                _samples[lo].Y + ((_samples[hi].Y - _samples[lo].Y) * t));
        }

        /// <summary>点到这条曲线的最近距离，按现成的采样折线量 —— 命中测试因此与绘制读同一张表。</summary>
        public double DistanceTo(Point p)
        {
            if (_length <= 0)
            {
                return double.MaxValue;
            }

            double best = double.MaxValue;
            for (int i = 1; i < _samples.Length; i++)
            {
                double d = DistanceToSegment(p, _samples[i - 1], _samples[i]);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        private static double DistanceToSegment(Point p, Point a, Point b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len2 = (abx * abx) + (aby * aby);
            if (len2 < 0.0001)
            {
                return Distance(p, a);
            }

            double t = Math.Clamp((((p.X - a.X) * abx) + ((p.Y - a.Y) * aby)) / len2, 0, 1);
            return Distance(p, new Point(a.X + (t * abx), a.Y + (t * aby)));
        }

        private static double Distance(Point p, Point q)
        {
            double dx = p.X - q.X, dy = p.Y - q.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>[from, to] 这一段弧长上的折线几何：两端各自插值到精确位置，中间用现成采样点。</summary>
        public Geometry Segment(double from, double to)
        {
            to = Math.Min(to, _length);
            from = Math.Clamp(from, 0, _length);

            var points = new List<Point>(SampleCount + 2) { AtLength(from) };
            for (int i = 1; i < _samples.Length - 1; i++)
            {
                double l = _cumulative[i];
                if (l <= from || l >= to)
                {
                    continue;
                }

                points.Add(_samples[i]);
            }

            points.Add(AtLength(to));

            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
            figure.Segments.Add(new PolyLineSegment(points, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }
    }

    // 弧长表按链接缓存：端点一变就重建，否则每帧只是两次比较。链接被删时条目在 Links 变更里一起丢掉
    private readonly Dictionary<IWorkflowLinkViewModel, LinkCurve> _curves = new();

    // 虚拟连线（指针下那根橡皮筋）也有自己的一条曲线，复用同一个实例：同时只会有一根
    private readonly LinkCurve _virtualCurve = new();

    private LinkCurve CurveFor(IWorkflowLinkViewModel link, Point from, Point to)
    {
        if (!_curves.TryGetValue(link, out var curve))
        {
            curve = new LinkCurve();
            _curves[link] = curve;
        }

        curve.Ensure(from, to);
        return curve;
    }

    private static void DrawLink(DrawingContext dc, LinkCurve curve, Color color, double thickness)
    {
        var body = curve.Segment(0, curve.Length);

        // 管壁：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。
        // 圆头：两端才不像被截断的横截面
        dc.DrawGeometry(null, Capped(new Pen(CardPalette.Alpha(color, 0.10), thickness + 9)), body);
        dc.DrawGeometry(null, Capped(new Pen(CardPalette.Alpha(color, 0.16), thickness + 4)), body);
        dc.DrawGeometry(null, Capped(new Pen(CardPalette.Alpha(color, 0.55), thickness)), body);
    }

    private static Pen Capped(Pen pen)
    {
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        return pen;
    }

    /// <summary>
    /// 彗星：沿弧长切出 [头−尾, 头] 这一段，再分若干小段画，每段一个递减的透明度与一个向白偏的颜色。
    /// <para>
    /// 不用渐变刷有两个理由，第二个才是新的：本平台的渐变写停靠点不出帧（见 flow 声明），所以原先那版
    /// 流光只能画成「裁剪几何 + 宽度动画」；而且渐变刷的轴是两端之间的直线，在曲线上会把光打偏 ——
    /// 亮度不再跟着弯走，绕弯时看着忽快忽慢。改成按弧长切出来的几何之后，这两条一起消失：
    /// 几何本来就是切出来的，不再依赖渐变刷。
    /// </para>
    /// </summary>
    private void DrawComet(DrawingContext dc, LinkCurve curve, Color color, double thickness)
    {
        double intensity = CometIntensity(FlowPhase);
        if (intensity <= 0.004 || curve.Length <= 0)
        {
            return;
        }

        double head = Math.Clamp(FlowPhase, 0, 1) * curve.Length;
        double tail = TailFraction * curve.Length;

        // 两遍：先光晕（更宽更淡）再本体，两遍都跟着头走，所以动感在光晕上也读得出来。
        // 光晕只跟亮头那半段：尾梢那半边按 f0² 已经淡到看不见，画它只是白开几何
        for (int pass = 0; pass < 2; pass++)
        {
            bool bloom = pass == 0;

            for (int k = bloom ? TailSegments / 2 : 0; k < TailSegments; k++)
            {
                double f0 = k / (double)TailSegments;      // 0 = 尾梢，1 = 头
                double f1 = (k + 1) / (double)TailSegments;

                double l0 = head - (tail * (1 - f0));
                double l1 = head - (tail * (1 - f1));
                if (l1 <= 0 || l0 >= curve.Length)
                {
                    continue;
                }

                // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
                double a = intensity * f0 * f0;
                if (a <= 0.004)
                {
                    continue;
                }

                // 尾梢是本体的颜色，越靠近头越白 —— 白热只发生在头部
                var c = Mix(color, Colors.White, f0);

                dc.DrawGeometry(null,
                    bloom
                        ? new Pen(CardPalette.Alpha(c, a * 0.22), thickness + 9)
                        : Capped(new Pen(CardPalette.Alpha(c, a), thickness * (0.45 + (0.95 * f0)))),
                    curve.Segment(l0, l1));
            }
        }
    }

    /// <summary>
    /// 一个周期里彗星有多亮：头部从发送端出发时升起来（占前 30% 路程），到达接收端之前落下去
    /// （占后 22%）。两端都是「没有光」，所以循环接缝看不出来。
    /// </summary>
    /// <remarks>
    /// Avalonia 那版把这三段写成三条相位（成形 / 行进 / 退去）。这里从一个相位推出来，是因为本平台
    /// 一个控件上只能跑一条转换（<c>Transition.Exit</c> 按目标停，两条会互相打断），相位一多就必然要
    /// 第二条 —— 所以只留一个 double，其余全从它算。
    /// </remarks>
    private static double CometIntensity(double phase)
    {
        double p = Math.Clamp(phase, 0, 1);
        return Math.Min(1, p / HeadFormed) * Math.Min(1, (1 - p) / ExitSpan);
    }

    // 两色之间线性混合（含 alpha），用于尾梢到头部那一段渐变
    private static Color Mix(Color from, Color to, double t)
    {
        byte L(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));

        return Color.FromArgb(L(from.A, to.A), L(from.R, to.R), L(from.G, to.G), L(from.B, to.B));
    }

    // ── Flow: one clock, and everything on the surface is a function of it ──

    // 弧长表的分辨率。128 段放到缩放上限（Layout.Scale 0.1，即放大约十倍）也看不出折线感
    private const int SampleCount = 128;

    // 拖尾占全长的比例。彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个透明度，衰减因此是连续的，也就不需要渐变刷
    private const int TailSegments = 16;

    // 头部走到这个比例时已经升到全亮 —— 从发送端出发的那一段
    private const double HeadFormed = 0.30;

    // 头部走到这里开始收暗，到终点正好归零 —— 循环接缝才看不出来
    private const double HeadLeaving = 0.78;

    private const double ExitSpan = 1.0 - HeadLeaving;

    // 一个周期。波纹与彗星共用它：两者都由同一个相位推出来，见 FlowPhase
    private static readonly TimeSpan FlowPeriod = TimeSpan.FromMilliseconds(2300);

    // 周期是否在跑，停只要停一次
    private bool _running;

    private double _flowPhase;

    /// <summary>
    /// 表面的相位，0 到 1 循环：<b>这是全表面唯一的动画状态</b>，沿链接跑的彗星与端口上的波纹都从它推出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一个 double 而不是几个，有两个理由。其一，本平台一个控件上只能跑一条转换（<c>Transition.Exit</c>
    /// 按目标停，两条会互相打断），相位一多就必然要有第二条 —— 唯一能做的就是让它们全是同一个相位的函数。
    /// 其二，它属于表面而不是属于链接：这个表面一笔画完所有链接、又画完所有端口（端口也归它画，
    /// 理由见 DrawPorts），没有 per-link 的视图可写，所以这一个成员就是全部的动画状态。
    /// 写它就重绘，因为画它的是表面自己的 OnRender / OnPostRender，没有别人会告诉它光走了 ——
    /// 一帧一次重绘是「动画画在表面自己身上」的代价，转换本来就按 60fps 走。
    /// </para>
    /// <para>
    /// 2300ms 这个周期照的是 Avalonia 那版端口的波纹；彗星因此一个周期走完整条链接（那边是三段相位拼出
    /// 1600ms）。两个动效一个节拍读起来是同一件事在发生，代价是彗星比 Avalonia 慢一档 ——
    /// 这是「一个控件一条时钟」的必然结果，不是随手挑的数。
    /// </para>
    /// </remarks>
    public double FlowPhase
    {
        get => _flowPhase;
        set
        {
            _flowPhase = value;
            InvalidateVisual();
        }
    }

    // 整个表面一条声明而非每条链接一条：写的都是表面自己的值，端点全常量故可 static readonly；匀速故不用缓动
    // LoopTime = int.MaxValue 是这套系统唯一的「永久」，且时长不能为零 —— 零长度的一趟不消耗时间，
    // 于是永久循环会空转而不是循环，Transition.Exit 也就再打断不了它
    // 本 build 只认每帧变化的几何——渐变写停靠点/换整组/移轴/每帧新刷子截图全同（实测），
    // 所以流光画成按弧长切出来的一段几何，不靠渐变刷（见 DrawComet）
    private static readonly Transition<NodeEditorSurface> Flow =
        Transition<NodeEditorSurface>.Create()
            .Property(s => s.FlowPhase, 1d)
            .Effect(new TransitionEffect()
            {
                Duration = FlowPeriod,
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

    // 起周期：相位回到 0
    // 转换从目标读起值，Execute 前要先回到周期起点；循环在每个接缝重放这份抓到的起值
    private void StartFlow()
    {
        FlowPhase = 0;
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

    // ── Hit testing (world coords) ─────────────────────────────────────────

    /// <summary>
    /// 端口的抓取半径（画布单位）：跟着字形一起被缩放 —— 口本身就是按设计尺寸画的，
    /// 缩到很远时还按固定像素去抓，就会出现「看得见的口抓不住、看不见的地方抓着空」。
    /// 输出口给得比输入口小一点：拉线从输出口起，误抓一颗粒代价比多试一次大。
    /// </summary>
    private double PortHitRadius(IWorkflowNodeViewModel node, double factor)
    {
        _cards.TryGetValue(node, out var card);
        double scale = card is null ? 1 : PortScale(node, card);
        return Math.Max(6, (NodePorts.PortBoxSize(node) / 2) * scale * factor);
    }

    private (IWorkflowNodeViewModel Node, int OutputIndex)? HitTestOutputPort(Point pos)
    {
        if (_tree is null) return null;
        for (int n = _tree.Nodes.Count - 1; n >= 0; n--)
        {
            var node = _tree.Nodes[n];
            var outputs = NodePorts.Outputs(node);
            double radius = PortHitRadius(node, 0.75);
            for (int i = 0; i < outputs.Count; i++)
            {
                var c = GetOutputPortCenter(node, i);
                double dx = pos.X - c.X, dy = pos.Y - c.Y;
                if (dx * dx + dy * dy <= radius * radius)
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
            double radius = PortHitRadius(node, 0.9);
            for (int i = 0; i < inputs.Count; i++)
            {
                var c = InputPortCenter(node, i);
                double dx = pos.X - c.X, dy = pos.Y - c.Y;
                if (dx * dx + dy * dy <= radius * radius)
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
        if (_tree is null)
        {
            return;
        }

        // 右键只在连线上有含义（弹出删除菜单），落在别处什么也不做：这一家的自动化右键路径不认单条连线，
        // 菜单由表面代开（见 OnLinkRightClick）
        if (e.ChangedButton == MouseButton.Right)
        {
            OnLinkRightClick(e);
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
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

            // 没在拖任何东西：只更新端口与连线的悬停。端口小，没有这点反馈就不知道指针到底有没有搭上它；
            // 连线长，它的悬停同时就是「当前选中」，Delete 与右键菜单读的是它
            default:
                var canvasPos = e.GetPosition(this);
                UpdatePortHover(canvasPos);
                UpdateLinkHover(canvasPos);
                break;
        }
    }

    /// <summary>指针落在哪颗端口上（画布坐标进，插槽视图模型出）。只改状态、只在真的换了口时重绘。</summary>
    private void UpdatePortHover(Point canvasPos)
    {
        if (_tree is null)
        {
            return;
        }

        var world = new Point(canvasPos.X - OriginX, canvasPos.Y - OriginY);
        IWorkflowSlotViewModel? hovered = null;

        if (HitTestOutputPort(world) is { } output)
        {
            var outputs = NodePorts.Outputs(output.Node);
            hovered = output.OutputIndex < outputs.Count ? outputs[output.OutputIndex].Slot : null;
        }
        else if (HitTestInputPort(world) is { } input)
        {
            var inputs = NodePorts.Inputs(input.Node);
            hovered = input.InputIndex < inputs.Count ? inputs[input.InputIndex].Slot : null;
        }

        if (ReferenceEquals(hovered, _hoverSlot))
        {
            return;
        }

        _hoverSlot = hovered;
        InvalidateVisual();
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

    // 端口的状态现在由 DrawPorts 在绘制时现算（每个口问一次），端口画在表面上就没有「推给卡片」那一步，
    // 也就不需要一份缓存的数组去记它。
    //
    // 口径与本平台的惯例一致 —— 节点视图模型自己维护 State（Trimmed demo 直接读 slot.State）——
    // 再把拖拽预览那条补上：正在拉线的那颗输出口是 PreviewSender，指针压着的那颗输入口是 PreviewReceiver。
    // 预览刻意不写成 Sender/Receiver：那两个是「已经连上了」的语义（Tomato / Lime），
    // 而正在拉的那一头在 Avalonia 那套设计里是保持白色的 —— 它的反馈在呼吸与波纹上，不在颜色上。

    private SlotState OutputPortState(IWorkflowNodeViewModel node, int outputIndex)
    {
        var state = SlotState.StandBy;
        if (_dragFrom is { } f && f.Node == node && f.OutputIndex == outputIndex)
        {
            state |= SlotState.PreviewSender;
        }

        if (IsSenderPort(node, outputIndex))
        {
            state |= SlotState.Sender;
        }

        return state;
    }

    private SlotState InputPortState(IWorkflowNodeViewModel node, int inputIndex)
    {
        var state = SlotState.StandBy;
        if (_dropTarget is { } t && t.Node == node && t.InputIndex == inputIndex)
        {
            state |= SlotState.PreviewReceiver;
        }

        if (IsReceiverPort(node, inputIndex))
        {
            state |= SlotState.Receiver;
        }

        return state;
    }

    private bool IsSenderPort(IWorkflowNodeViewModel node, int outputIndex)
    {
        if (_tree is null)
        {
            return false;
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
