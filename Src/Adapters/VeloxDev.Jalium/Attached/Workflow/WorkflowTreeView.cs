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
    private IDisposable? _treeEvents;
    private TreeEventSink? _eventSink;
    private ScrollViewer? _scrollViewer;

    // 连线输入归 Core（每棵树一个，见 WorkflowInput.For）：本类只订右键按下弹菜单，命中与删除仍由
    // 输入路由自己完成（悬停外观是宿主的）。菜单每次右键现建、收起即弃 —— 复用一份会带着上一次那条链接的捕获。
    private WorkflowInput? _input;
    private ContextMenu? _linkMenu;

    // 这份菜单指着的那条线。收起请求会带着 hub 记的那条线来，比对上才收 —— 同刻只会开一份菜单，但判定照守。
    private IWorkflowLinkViewModel? _menuLink;

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

        // 指针离开表面、或按键冒泡到表面（卡片里的控件拿去焦点时），都翻译成 Core 的输入事件转给 hub。
        // 表面必须能拿焦点：Delete 只有这一条到达本层的路（Trimmed demo 与生成出来的树视图都没有
        // 窗口级兜底）。代价是平台会把「整块画布」卷进视口、画布跳原点 —— 所以在发源地吃掉
        // 「目标是自己」的那一次请求，卡片里控件的请求照旧上冒。
        Focusable = true;
        AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnRequestBringIntoView));
        MouseLeave += OnMouseLeave;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnSurfaceKeyDown));
        AddHandler(KeyUpEvent, new KeyEventHandler(OnSurfaceKeyUp));
        AddHandler(Mouse.MouseWheelEvent, new MouseWheelEventHandler(OnSurfaceMouseWheel));
    }

    // 只拦目标是自己（整块画布）的那一次滚进视口；卡片里的控件发起的请求照旧往上冒
    private void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (ReferenceEquals(e.TargetObject, this))
        {
            e.Handled = true;
        }
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
        _treeEvents?.Dispose();
        _treeEvents = null;
        DetachInteraction();
        _tree = tree;
        if (_tree is null)
        {
            return;
        }

        AttachInteraction();

        // 模型事件由 Core 的 relay 接一次，转发到本类的可重写钩子；Helper 不提供事件时 Attach 返回 null。
        _treeEvents = WorkflowEventRelay.Attach(_tree, EventSink);

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

    /// <summary>Called before a connection is made; refuse that drag via the argument's handle.</summary>
    /// <param name="e">The two ports the connection would join.</param>
    protected virtual void OnConnecting(ConnectionEventArgs e) { }

    /// <summary>Called once the link exists.</summary>
    /// <param name="e">The two ports the connection joined.</param>
    protected virtual void OnConnected(ConnectionEventArgs e) { }

    /// <summary>
    /// Fills the context menu the surface shows for a link. The base adds a single <c>Delete</c> item; override it
    /// to add or remove entries.
    /// </summary>
    /// <param name="menu">The menu being built; the surface shows it once this returns.</param>
    /// <param name="link">The link the menu is about.</param>
    /// <remarks>
    /// The menu is built anew for every right press, so an edit here takes effect the next time it opens. The
    /// surface opens it only when a link was hit; a right press on empty canvas raises the request with no link
    /// and is not shown.
    /// </remarks>
    protected virtual void OnBuildLinkMenu(ContextMenu menu, IWorkflowLinkViewModel link)
    {
        var item = new MenuItem { Header = "Delete" };
        item.Click += (_, _) =>
        {
            // 本平台的菜单项点完不自己收：不显式关，删掉连线后菜单还杵在画布上挡着看得见的东西。
            menu.Close();
            link.DeleteCommand.Execute(null);
        };
        menu.Items.Add(item);
    }

    // 换绑定树时复用同一个 sink；sink 只把调用转给可重写钩子，不持有额外状态。
    private TreeEventSink EventSink => _eventSink ??= new TreeEventSink(this);

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
        // 与其它 GUI 一致的三次贝塞尔（镜像 workflow-link-view）：两个控制点各自水平拉开。
        // 最小拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段。
        var pull = Math.Max(40, Math.Abs(to.X - from.X) * 0.5);
        var c1 = new Point(from.X + pull, from.Y);
        var c2 = new Point(to.X - pull, to.Y);
        var figure = new PathFigure { StartPoint = from, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new BezierSegment(c1, c2, to, true));
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

    // ── 连线交互（转发给 Core）────────────────────────────────────────────

    // 指针位置就是表面自己的坐标（= 画布塌缩系），与连线视图经 WorkflowLinkAttachment 发布曲线用的是同一个系。
    // 拖拽中（节点/连线/平移）不转发移动，免得沿途把经过的实连线点亮。
    // 命中由共享的曲线判定器回答（这一家的连线是表面一笔画出来的、没有每线的可视对象），事件交给输入路由。
    private void RoutePointer(Point canvasPos, Func<Anchor, IWorkflowViewModel?, WorkflowEventHandle, WorkflowPointerEventArgs> args)
    {
        if (_input is not { } input) return;

        var anchor = new Anchor(canvasPos.X, canvasPos.Y, 0);
        var target = input.Tree.HitTestVisibleLinks(anchor.Horizontal, anchor.Vertical, input.HitRadius);

        input.Route(args(anchor, target, new WorkflowEventHandle()));

        // 命中一条线就把键盘焦点收到表面：Delete 才进得来（同其它六家）。
        if (input.HoveredLink is not null && Focusable) Focus();
    }

    private static WorkflowMouseButton ButtonOf(MouseButton button) => button switch
    {
        MouseButton.Left => WorkflowMouseButton.Left,
        MouseButton.Right => WorkflowMouseButton.Right,
        MouseButton.Middle => WorkflowMouseButton.Middle,
        MouseButton.XButton1 => WorkflowMouseButton.XButton1,
        MouseButton.XButton2 => WorkflowMouseButton.XButton2,
        _ => WorkflowMouseButton.None,
    };

    private static InputModifiers Modifiers(ModifierKeys keys)
    {
        var modifiers = InputModifiers.None;
        if (keys.HasFlag(ModifierKeys.Alt)) modifiers |= InputModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Control)) modifiers |= InputModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Shift)) modifiers |= InputModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) modifiers |= InputModifiers.Meta;
        return modifiers;
    }

    // 键按字母/数字/功能键三段连续区间做算术映射（两边枚举的这几段都是连续的），其余逐个点名，没点到的报 Unknown。
    private static WorkflowKey ToKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return WorkflowKey.A + (key - Key.A);
        if (key >= Key.D0 && key <= Key.D9) return WorkflowKey.D0 + (key - Key.D0);
        if (key >= Key.F1 && key <= Key.F12) return WorkflowKey.F1 + (key - Key.F1);

        return key switch
        {
            Key.None => WorkflowKey.None,
            Key.Cancel => WorkflowKey.Cancel,
            Key.Back => WorkflowKey.Back,
            Key.Tab => WorkflowKey.Tab,
            Key.Enter => WorkflowKey.Enter,
            Key.Escape => WorkflowKey.Escape,
            Key.Space => WorkflowKey.Space,
            Key.PageUp => WorkflowKey.PageUp,
            Key.PageDown => WorkflowKey.PageDown,
            Key.End => WorkflowKey.End,
            Key.Home => WorkflowKey.Home,
            Key.Left => WorkflowKey.Left,
            Key.Up => WorkflowKey.Up,
            Key.Right => WorkflowKey.Right,
            Key.Down => WorkflowKey.Down,
            Key.Insert => WorkflowKey.Insert,
            Key.Delete => WorkflowKey.Delete,
            _ => WorkflowKey.Unknown,
        };
    }

    // 订阅与解订输入面：本体只碰右键按下弹菜单，删除仍由路由的 AutoDelete 完成。
    private void AttachInteraction()
    {
        DetachInteraction();
        if (_tree is not { } tree) return;

        _input = WorkflowInput.For(tree);
        if (tree.GetHelper() is IWorkflowInputEvents events)
        {
            events.Input.PointerPressed += OnLinkPointerPressed;
        }

        tree.GetHelper().LinkRemoved += OnLinkRemoved;
    }

    private void DetachInteraction()
    {
        if (_input is not { } input) return;

        var helper = input.Tree.GetHelper();
        if (helper is IWorkflowInputEvents events) events.Input.PointerPressed -= OnLinkPointerPressed;
        helper.LinkRemoved -= OnLinkRemoved;

        // 换树/解绑时菜单还开着就先收：Closed 会顺手把挂起放开。
        _linkMenu?.Close();
        _input = null;
    }

    // 右键菜单归表面、不归连线视图：右键落在表面上（这一家的连线是表面一笔画出来的、不吃指针），
    // 而弹出要根视觉（窗口客户区）坐标、模型给的是画布坐标 —— 只有表面同时知道这两件事。
    // 否决归 Preview 相（ContextMenuRequesting）：到得了这里的请求必是没被拒绝的，所以这里不查 Handle。
    // 条目由 OnBuildLinkMenu 给出（基类默认一项 Delete）。
    private void OnLinkPointerPressed(object? sender, WorkflowPointerPressedEventArgs e)
    {
        if (e.Button != WorkflowMouseButton.Right) return;
        if (e.Target is not IWorkflowLinkViewModel link) return;

        // 链上更靠前的一级（连线自己）可以否决这次按下 —— 它说不给菜单，这里就不给。
        if (e.Handle.PreventDefault) return;
        if (_linkMenu?.IsOpen == true) return;

        var menu = new ContextMenu();
        OnBuildLinkMenu(menu, link);

        menu.Closed += (_, _) =>
        {
            // 收起即放开挂起：宿主自己记这一笔账，输入路由只管照做。
            if (_input is { } input) input.IsSuspended = false;
            if (ReferenceEquals(_linkMenu, menu)) _linkMenu = null;
            if (ReferenceEquals(_menuLink, link)) _menuLink = null;
        };

        _linkMenu = menu;
        _menuLink = link;

        // 菜单一开指针就飞到弹层上去：先把指针跟踪挂起，那之后的移动不会清掉这次选中的线。
        if (_input is { } opening) opening.IsSuspended = true;
        menu.Open(ToMenuPosition(e.Position));
    }

    // 菜单指着的那条线已经不在树上：收起这份菜单（树报的是离场的那条，本家只认自己这份）。
    private void OnLinkRemoved(object? sender, IWorkflowLinkViewModel link)
    {
        if (!ReferenceEquals(_menuLink, link)) return;
        _linkMenu?.Close();
    }

    // e.Position 是表面自己的坐标（指针位置的发布就是原样转发的表面坐标），换成屏幕坐标、再回到
    // ContextMenu.Open 要的根视觉坐标 —— 菜单最终由平台的 Popup 换算成屏幕位置。
    private Point ToMenuPosition(Anchor position)
    {
        var screen = PointToScreen(new Point(position.Horizontal, position.Vertical));
        return VisualTreeHelper.GetRoot(this) is Visual root ? root.PointFromScreen(screen) : screen;
    }

    private void OnMouseLeave(object? sender, MouseEventArgs e)
    {
        if (_tree is not { } tree) return;

        // 真正离开表面才报 Exited：菜单开着时那一发会被输入路由按挂起忽略，表面不必再挡一次。
        WorkflowInput.For(tree).Route(new WorkflowPointerExitedEventArgs(
            new Anchor(), InputModifiers.None, this, null, new WorkflowEventHandle()));
    }

    // Delete 只在指针下有连线时才转发：键从卡片里的控件冒泡上来也一样，路由会据指针目标裁决删哪条
    private void OnSurfaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_input is not { } input) return;
        if (input.HoveredLink is null) return;

        input.Route(new WorkflowKeyDownEventArgs(
            ToKey(e.Key), (int)e.Key, Modifiers(e.KeyboardModifiers), false, this, input.HoveredLink, new WorkflowEventHandle()));

        if (e.Key == Key.Delete) e.Handled = true;
    }

    private void OnSurfaceKeyUp(object? sender, KeyEventArgs e)
    {
        if (_input is not { } input) return;

        input.Route(new WorkflowKeyUpEventArgs(
            ToKey(e.Key), (int)e.Key, Modifiers(e.KeyboardModifiers), false, this, input.HoveredLink, new WorkflowEventHandle()));
    }

    // 滚轮也进输入路由（Ctrl+滚轮是缩放，归宿主自己的预览处理器，两者不重叠）。
    private void OnSurfaceMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (_input is null || e.KeyboardModifiers.HasFlag(ModifierKeys.Control)) return;

        RoutePointer(e.GetPosition(this), (p, t, h) => new WorkflowPointerWheelEventArgs(
            p, Modifiers(e.KeyboardModifiers), this, t, 0d, e.Delta / 120d, h));
    }

    // ── 鼠标交互（基于模型）────────────────────────────────────────────────

    private void OnMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (_tree is null) return;

        // 右键只在连线上有含义：转发按下让 hub 裁决（命中在 Core）；命中了 hub 才报
        // ContextMenuRequested，本表面据此弹菜单（条目见 OnBuildLinkMenu）。这里不置 Handled ——
        // 占掉这一下只会挡住宿主的默认右键路径
        if (e.ChangedButton == MouseButton.Right)
        {
            RoutePointer(e.GetPosition(this), (p, t, h) => new WorkflowPointerPressedEventArgs(
                p, Modifiers(e.KeyboardModifiers), this, t, WorkflowMouseButton.Right, 1, h));
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            RoutePointer(e.GetPosition(this), (p, t, h) => new WorkflowPointerPressedEventArgs(
                p, Modifiers(e.KeyboardModifiers), this, t, WorkflowMouseButton.Middle, 1, h));
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        // 左键按下也进输入路由（本家从不转发它 —— 拖拽与端口手势把这一下吃在这里），不置 Handled：
        // 下面那串命中照常跑，谁都不挡。
        RoutePointer(e.GetPosition(this), (p, t, h) => new WorkflowPointerPressedEventArgs(
            p, Modifiers(e.KeyboardModifiers), this, t, WorkflowMouseButton.Left, 1, h));

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

            // 没在拖任何东西：指针移动交给输入路由，悬停落在哪条线上由共享的曲线命中裁决。
            // 悬停到连线上时把焦点收到表面 —— 这是 Delete 到达本层的唯一路径；跳原点的问题已在
            // 构造里被 RequestBringIntoView 那道闸挡住
            default:
                RoutePointer(e.GetPosition(this), (p, t, h) => new WorkflowPointerMovedEventArgs(
                    p, Modifiers(e.KeyboardModifiers), this, t, h));
                break;
        }
    }

    private void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        RoutePointer(e.GetPosition(this), (p, t, h) => new WorkflowPointerReleasedEventArgs(
            p, Modifiers(e.KeyboardModifiers), this, t, ButtonOf(e.ChangedButton), 1, h));

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

    // relay 的宿主：把每个 sink 方法原样转给对应的 protected virtual，派生类只需重写钩子。
    private sealed class TreeEventSink(WorkflowTreeView owner) : IWorkflowTreeEventSink
    {
        public void OnConnecting(ConnectionEventArgs e) => owner.OnConnecting(e);

        public void OnConnected(ConnectionEventArgs e) => owner.OnConnected(e);
    }
}
