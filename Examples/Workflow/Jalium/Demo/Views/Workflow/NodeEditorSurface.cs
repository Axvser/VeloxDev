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
    private const double LinkThickness = 2;

    // 选中（＝指针搭上）的连线：加粗 1.5，与其它六家的连线一致
    private const double SelectedLinkThickness = LinkThickness + 1.5;

    // 控制点的最小水平拉出量（画布单位）：两个端口靠得很近时 0.5·dx 会让曲线退化成一条直线段，
    // 失去「从端口水平出来」的形状。发布给 Core 的曲线与这里画的是同一个值
    private const double PullMinimum = 40;

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

    // 当前被点亮的连线。链接和端口一样是表面画的，没有控件能担任这个角色；
    // 悬停即点亮（与其它六家一致），它只决定绘制时给哪条上高亮色。
    // 由 hub 的 HoverChanged 驱动（见 OnLinkHoverChanged），表面自己不再判命中；Delete 由 hub 按 HoveredLink 裁决
    private IWorkflowLinkViewModel? _selectedLink;

    // 连线的命中与输入裁决归 Core（见 LinkInteraction）：表面只负责把指针/按键翻译成标准输入事件转发进去，
    // 并订阅结果（选中上色、右键菜单、删除请求）。命中的算法不在这家
    private LinkInteraction? _interaction;

    // 右键菜单每次现建、收起即弃 —— 复用一份会带着上一次那条链接的捕获；同时只会开一个，
    // 字段只用来挡住重复请求与换树时收尾
    private ContextMenu? _linkMenu;

    // 这份菜单指着的那条线。收起请求会带着 hub 记的那条线来，比对上才收 —— 同刻只会开一份菜单，但判定照守。
    private IWorkflowLinkViewModel? _menuLink;

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

        // 表面自己画链接与端口，故只有它能持有动画；相位是整个表面一个周期，生命周期就这两处
        // Loaded 起、Unloaded 停（见下面的 flow 区）
        Loaded += (_, _) => StartFlow();

        Unloaded += (_, _) => StopFlow();

        // 指针离开表面：端口的悬停反馈是表面画的（这里直接清），连线的悬停转成 Core 的 PointerExited。
        // 菜单开着时那一发不会清掉选中 —— hub 自己认挂起（见 Publish(ContextMenuEvent)）
        MouseLeave += (_, _) =>
        {
            _hoverSlot = null;
            ForwardPointer(PointerPhase.Exited, default);
            InvalidateVisual();
        };
    }

    // ── Link selection: hover highlight, the Delete key, the right-click menu ──

    // 选中＝指针搭上的那条，换成白色并加粗。七家统一用白：它读起来是「这条线被点亮了」，而不是「被重新
    // 上了个色」；红像告警、青像另一条线，都试过，用户否掉了
    private static readonly Color SelectedLinkColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Delete the link currently under the pointer, as a <c>Delete</c> key press does.
    /// Returns whether there was one to delete.</summary>
    public bool DeleteSelectedLink()
    {
        // 「哪条在指针下」归 hub：窗口级预览走的就是这一条，与表面自己的 KeyDown 得到同一个答案
        if (_interaction?.HoveredLink is not { } link)
        {
            return false;
        }

        DeleteLink(link);
        return true;
    }

    // 宿主侧的删除（窗口级 Delete、菜单项）：走连线自己的命令，删完把指向它的选中清掉。
    // 表面自己的 KeyDown 不走这里 —— 它把键转发给 hub，由 hub 的 AutoDelete 执行命令，
    // 集合变更后再由 PruneCurves 收拾选中
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

        link.DeleteCommand.Execute(null);
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // 键也过 Core：「现在按 Delete 删哪条」因此与其它六家是同一个答案，不靠表面另记一个选中
        if (e.Key != Key.Delete || _interaction?.HoveredLink is null)
        {
            return;
        }

        _interaction.Publish(new KeyEvent(InputKey.Delete));
        e.Handled = true;
    }

    // 只拦目标是自己（整块画布）的那一次滚进视口；节点卡里的控件发起的请求照旧往上冒
    private void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (ReferenceEquals(e.TargetObject, this))
        {
            e.Handled = true;
        }
    }

    // ── Link interaction (forwarded to Core) ───────────────────────────────

    // hub 归 Core（同树同实例，见 LinkInteraction.For）—— 表面不持有实例，只把事件转进去、订它的结果。
    // 这家一个表面画完所有线、没有「每线的可视对象」，所以 hub 的 AutoHighlight 没有东西可点：选中由这里
    // 订阅 HoverChanged 自己画（别家是视觉实现 ILinkHighlight、由 hub 直接点亮）。删除归 hub 的 AutoDelete，
    // 本家不再订 LinkDeleteRequested。
    private void AttachInteraction()
    {
        DetachInteraction();
        if (_tree is null)
        {
            return;
        }

        var hub = LinkInteraction.For(_tree);
        hub.HoverChanged += OnLinkHoverChanged;
        hub.ContextMenuRequested += OnContextMenuRequested;
        hub.ContextMenuDismissRequested += OnContextMenuDismissRequested;
        _interaction = hub;
    }

    private void DetachInteraction()
    {
        if (_interaction is not { } hub)
        {
            return;
        }

        hub.HoverChanged -= OnLinkHoverChanged;
        hub.ContextMenuRequested -= OnContextMenuRequested;
        hub.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
        // 换树/解绑时菜单还开着就先收：Closed 会顺手把 hub 的挂起放开
        _linkMenu?.Close();
        _interaction = null;
    }

    // 指针位置就是表面自己的坐标（= ToCanvas 的画布系），与发布给 Core 的曲线用的是同一个系。
    // 菜单开着时那几帧指针在弹层上，转发进去的移动会被 hub 按挂起忽略（Opened 已报过）
    private void ForwardPointer(PointerPhase phase, Point canvasPos, PointerButtonKind button = PointerButtonKind.None)
    {
        if (_interaction is not { } hub)
        {
            return;
        }

        hub.Publish(new PointerEvent(phase, new Anchor(canvasPos.X, canvasPos.Y, 0), button));
    }

    // 悬停即「当前选中」，移开即取消 —— 与其它六家的连线一致
    private void OnLinkHoverChanged(object? sender, LinkHoverEventArgs e)
    {
        _selectedLink = e.Link;
        InvalidateVisual();
    }

    // 右键菜单归表面：这一家的连线是表面一笔画出来的、不吃指针，右键也落在表面上，
    // 而弹出要根视觉坐标、模型给的是画布坐标 —— 只有表面同时知道这两件事。
    // 否决归 hub 的 Preview 相（ContextMenuRequesting），到得了这里的请求必是没被拒绝的。
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (e.Link is not { } link) return;
        if (_linkMenu?.IsOpen == true) return;

        var menu = new ContextMenu();
        OnBuildLinkMenu(menu, link);

        menu.Closed += (_, _) =>
        {
            // 收起报回 hub：它自己放开挂起，表面不用记这一笔账
            _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, e.Position, link));
            if (ReferenceEquals(_linkMenu, menu)) _linkMenu = null;
            if (ReferenceEquals(_menuLink, link)) _menuLink = null;
        };

        _linkMenu = menu;
        _menuLink = link;

        // 菜单一开指针就飞到弹层上去：先报 Opened，hub 把悬停挂起，那之后的移动不会清掉这次选中的线
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, e.Position, link));
        menu.Open(ToMenuPosition(e.Position));
    }

    // 菜单指着的那条线已经不在树上：hub 请宿主收起这份菜单（它收不了宿主的弹窗）。收起照常报 Closed，挂起随之放开。
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        _linkMenu?.Close();
    }

    // 条目在这里增删。与 WorkflowTreeView 基类的 OnBuildLinkMenu 同一角色：这块画布是自绘的 Canvas、
    // 不是 WorkflowTreeView，拿不到那个 protected 钩子，所以自带一份同形的扩展点（条目与 Trimmed demo 一致）。
    private void OnBuildLinkMenu(ContextMenu menu, IWorkflowLinkViewModel link)
    {
        var item = new MenuItem { Header = "Delete" };
        item.Click += (_, _) =>
        {
            // 这一家的菜单项点完不自己收：不显式关，删掉连线后菜单还杵在画布上挡着看得见的东西
            menu.Close();
            DeleteLink(link);
        };
        menu.Items.Add(item);
    }

    // e.Position 是表面自己的坐标；换成屏幕坐标、再回到根视觉坐标 —— 菜单最终由平台的 Popup 换算成屏幕位置
    private Point ToMenuPosition(Anchor position)
    {
        var screen = PointToScreen(new Point(position.Horizontal, position.Vertical));
        return VisualTreeHelper.GetRoot(this) is Visual root ? root.PointFromScreen(screen) : screen;
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

        AttachInteraction();
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

        DetachInteraction();
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

        // 这家不写 slot.Anchor：Core 的命中只认表面画出来时发布的那条曲线，anchor 不再是就绪证据
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

        // 悬停的那条也可能已经不在了：删除不止表面这一条路（Agent、Undo 都会删连线）。
        // 选中由 hub 的 HoverChanged 驱动，所以要清的是 hub 的悬停 —— 发一次 Exited 让它把选中一起带走，
        // 否则留着一条不在树上的选中，下一次 Delete 就打在空气上
        if (_interaction?.HoveredLink is { } hovered && !alive.Contains(hovered))
        {
            _interaction.Publish(new PointerEvent(PointerPhase.Exited, new Anchor()));
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
            var center = GetPortCenter(from.Node, from.OutputIndex);
            var start = ToCanvas(center.X, center.Y);
            var end = ToCanvas(_tree.VirtualLink.Receiver.Anchor.Horizontal, _tree.VirtualLink.Receiver.Anchor.Vertical);
            var preview = VirtualCurve(start, end);
            dc.DrawGeometry(null, s_virtualPen, LinkSegment(preview, 0, preview.Length));
        }

        // 标尺带视口固定（绝对浮贴），排在最后：滚动时它永远贴着视口，也永远在最上面
        DrawRulers(dc);
    }

    // ── Ports (the ring + its ripples) ─────────────────────────────────────

    // 画一个节点上的每颗端口：字形（环 + 芯）与它的端口名。
    // 为什么端口在表面上画，而不是像 Avalonia 那样放进卡片自己：这一家的渲染器按布局盒裁剪子元素
    // （Visual.ShouldRenderChild 只看布局盒、不看画出来的内容，见 Jalium Trimmed 那个 LinkView 的注释）——
    // 而 Enum 卡根上那个裁到圆角的主体区（NodeChrome 给卡片设了 ClipToBounds）会直接切掉它。
    // 所以字形、名字、连线三者不可能对不齐。
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

    // 端口名：行内那颗小字，居中在它那口的行上，往卡里缩 inset 设计单位。
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

    // 卡片当前被缩放的比例：Viewbox 是等比缩放的，取两轴的较小者才与它一致。
    private static double PortScale(IWorkflowNodeViewModel node, NodeViewBase card)
    {
        double sx = card.DesignWidth <= 0 ? 1 : node.Size.Width / card.DesignWidth;
        double sy = card.DesignHeight <= 0 ? 1 : node.Size.Height / card.DesignHeight;
        return Math.Min(sx, sy);
    }

    // 视口粗筛。端口画在表面自己的坐标里，不受「子元素布局盒」那套剔除影响，但逐个画整棵树的端口在
    // 缩到很远时是白费力气 —— 卡片在视口之外就整块跳过（留一点余量给外溢的那半个口）。
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
            // 端点每次绘制现读：拖节点只动 Anchor，链接本身收不到通知，所以几何按端点缓存。
            // 这条曲线同时发布给 Core 做命中，画出来的与能点中的是同一条（退化成一点的不发布，免得被当可命中）
            var curve = CurveFor(link, p0, p1);
            link.PublishCurve(curve.Length > 0 ? curve : null);
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

    // 一条链接是否可能出现在视口里。两点都落在视口外也照样可能穿过视口（贝塞尔是弯的），
    // 所以判的是两点连线的包围盒与视口相交，再往外留一段余量把控制点拉出去的弧度也算进去。
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

    // ── 链接几何（发布给 Core 的扁曲线 + 弧长取段）────────────────────────

    // 每条链接当前发布给 Core 的曲线。Core 的 LinkCurve 一旦建好就不可变，端点变了就换一条；
    // 缓存只为省下每帧的采样开销，判据就是建曲线时的那两个端点。
    private readonly Dictionary<IWorkflowLinkViewModel, (Point From, Point To, LinkCurve Curve)> _curves = new();

    // 虚拟连线（指针下那根橡皮筋）也有自己的一条曲线：同时只会有一根，端点一变就重算
    private (Point From, Point To, LinkCurve Curve)? _virtualPreview;

    // 建一条 Core 的扁曲线：控制点水平拉出 max(PullMinimum, 0.5·dx)，与这里画出来的是同一条贝塞尔。
    // 命中、取弧长都读它，表面不再自存一份采样表
    private static LinkCurve BuildCurve(Point from, Point to)
        => LinkCurve.BuildCubic(from.X, from.Y, to.X, to.Y, PullMinimum, LinkCurve.DefaultSampleCount);

    private LinkCurve CurveFor(IWorkflowLinkViewModel link, Point from, Point to)
    {
        if (_curves.TryGetValue(link, out var entry) && entry.From == from && entry.To == to)
        {
            return entry.Curve;
        }

        var curve = BuildCurve(from, to);
        _curves[link] = (from, to, curve);
        return curve;
    }

    private LinkCurve VirtualCurve(Point from, Point to)
    {
        if (_virtualPreview is { } entry && entry.From == from && entry.To == to)
        {
            return entry.Curve;
        }

        var curve = BuildCurve(from, to);
        _virtualPreview = (from, to, curve);
        return curve;
    }

    // 从 Core 的扁曲线切出 [from, to] 这段弧长的折线几何：两端各插值到精确位置，中间用现成采样点。
    // 几何由发布给 Core 的那条曲线切出来，画与取点因此共享同一张弧长表。
    // 用 PathGeometry + PolyLineSegment 而不是 StreamGeometry：后者的上下文接口在本平台没有文档。
    private static Geometry LinkSegment(LinkCurve curve, double from, double to)
    {
        to = Math.Min(to, curve.Length);
        from = Math.Clamp(from, 0, curve.Length);

        var points = new List<Point>(curve.Count + 2) { ToPoint(curve.PointAtLength(from)) };
        for (var i = 1; i < curve.Count - 1; i++)
        {
            var l = curve.LengthAt(i);
            if (l <= from || l >= to)
            {
                continue;
            }

            points.Add(new Point(curve.XAt(i), curve.YAt(i)));
        }

        points.Add(ToPoint(curve.PointAtLength(to)));

        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(points, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point ToPoint((double X, double Y) p) => new(p.X, p.Y);

    private static void DrawLink(DrawingContext dc, LinkCurve curve, Color color, double thickness)
    {
        var body = LinkSegment(curve, 0, curve.Length);

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

    // 彗星：沿弧长切出 [头−尾, 头] 这一段，再分若干小段画，每段一个递减的透明度与一个向白偏的颜色。
    // 不用渐变刷有两个理由，第二个才是新的：本平台的渐变写停靠点不出帧（见 flow 声明），所以原先那版
    // 流光只能画成「裁剪几何 + 宽度动画」；而且渐变刷的轴是两端之间的直线，在曲线上会把光打偏 ——
    // 亮度不再跟着弯走，绕弯时看着忽快忽慢。改成按弧长切出来的几何之后，这两条一起消失：
    // 几何本来就是切出来的，不再依赖渐变刷。
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
                    LinkSegment(curve, l0, l1));
            }
        }
    }

    // 一个周期里彗星有多亮：头部从发送端出发时升起来（占前 30% 路程），到达接收端之前落下去
    // （占后 22%）。两端都是「没有光」，所以循环接缝看不出来。
    // 一个控件上只能跑一条转换（Transition.Exit 按目标停，两条会互相打断），相位一多就必然要
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

    // 表面的相位，0 到 1 循环：这是全表面唯一的动画状态，沿链接跑的彗星与端口上的波纹都从它推出来。
    // 一个 double 而不是几个，有两个理由。其一，本平台一个控件上只能跑一条转换（Transition.Exit
    // 按目标停，两条会互相打断），相位一多就必然要有第二条 —— 唯一能做的就是让它们全是同一个相位的函数。
    // 其二，它属于表面而不是属于链接：这个表面一笔画完所有链接、又画完所有端口（端口也归它画，
    // 理由见 DrawPorts），没有 per-link 的视图可写，所以这一个成员就是全部的动画状态。
    // 写它就重绘，因为画它的是表面自己的 OnRender / OnPostRender，没有别人会告诉它光走了 ——
    // 一帧一次重绘是「动画画在表面自己身上」的代价，转换本来就按 60fps 走。
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

    // 端口的抓取半径（画布单位）：跟着字形一起被缩放 —— 口本身就是按设计尺寸画的，
    // 缩到很远时还按固定像素去抓，就会出现「看得见的口抓不住、看不见的地方抓着空」。
    // 输出口给得比输入口小一点：拉线从输出口起，误抓一颗粒代价比多试一次大。
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

        // 右键只在连线上有含义（弹出删除菜单），落在别处什么也不做：命中的裁决在 hub —— 按下转发进去，
        // 命中了它才发 ContextMenuRequested，表面订阅后弹菜单（见 OnContextMenuRequested）；空白处右键不置 Handled
        if (e.ChangedButton == MouseButton.Right)
        {
            ForwardPointer(PointerPhase.Pressed, e.GetPosition(this), PointerButtonKind.Right);
            if (_interaction?.HoveredLink is not null)
            {
                e.Handled = true;
            }

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

            // 没在拖任何东西：更新端口与连线的悬停。端口是表面自己画的，就地更新；连线的悬停转发给 hub，
            // 由 Core 裁决后经 HoverChanged 回到 OnLinkHoverChanged（悬停即选中）
            default:
                var canvasPos = e.GetPosition(this);
                UpdatePortHover(canvasPos);
                ForwardPointer(PointerPhase.Moved, canvasPos);
                break;
        }
    }

    // 指针落在哪颗端口上（画布坐标进，插槽视图模型出）。只改状态、只在真的换了口时重绘。
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
