using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using PlatformInput = System.Windows.Forms;
using Wf = VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms does not support attached properties, but this type mirrors the workflow surface API shape used by other adapters.
/// </summary>
public sealed class WorkflowSurfaceBehavior
{
    private sealed class SurfaceState : IMessageFilter
    {
        public bool IsEnabled { get; set; }
        public bool ZoomEnabled { get; set; }
        public Control? ScrollViewer { get; set; }
        public Control? Canvas { get; set; }
        public Control? GridDecorator { get; set; }
        public Control? MinimapOverlay { get; set; }
        public IWorkflowTreeViewModel? WorkflowTree { get; set; }

        // 挂在输入路由上的画布滚动口。本类拥有它，宿主拿它去滚；换树/解绑时连同旧树一起摘。
        public IWorkflowSurfaceScroller? Scroller { get; set; }

        // Scroller 当前挂在哪棵树上：换树/解绑时用它只摘自己挂的那一个，不动同一棵树上别的表面挂的。
        public IWorkflowTreeViewModel? ScrollerTree { get; set; }

        // 上一棵被挂上来的树（引用比较）。恢复只因「换了树」触发一次，之后的 Refresh 不再把用户滚回去。
        public IWorkflowTreeViewModel? LastRestoreTree { get; set; }
        public bool HasPendingRestore { get; set; }
        public bool RestoreQueued { get; set; }
        public double PendingRestoreX { get; set; }
        public double PendingRestoreY { get; set; }

        private const int WmMouseWheel = 0x020A;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmRightButtonDown = 0x0204;
        private const int WmMiddleButtonDown = 0x0207;

        // 过滤器归**启用**管，不归缩放管：关掉缩放的宿主一样要收到滚轮汇报（报告与手势是两件事）。
        private bool _filterAdded;

        internal void EnsureMessageFilter()
        {
            if (_filterAdded)
            {
                return;
            }

            Application.AddMessageFilter(this);
            _filterAdded = true;
        }

        internal void RemoveMessageFilter()
        {
            if (!_filterAdded)
            {
                return;
            }

            Application.RemoveMessageFilter(this);
            _filterAdded = false;
        }

        // 应用层预处理：滚轮发给谁取决于焦点，挂在控件上的处理器收不齐（实测三格零到达），所以按**指针底下**
        // 的控件认领。普通滚轮整笔归适配器 —— 路由之后执行默认竖滚，并吞掉消息，平台的滚动容器一次都不许
        // 滚画布；Ctrl+滚轮是缩放，同样在消息层接住。按下的那一支只把键盘焦点收到画布，消息照常放行。
        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            // 表面上任何一次按下（画布、卡片、插槽、连线）都先把键盘焦点收到画布，Ctrl+Z 这类树级按键才有
            // 路由。画布之外的按下（缩略图、HUD）与可编辑控件里的按下不动它 —— 见 FocusSurfaceForPress。
            if (m.Msg is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown)
            {
                FocusSurfaceForPress();
                return false;
            }

            if (m.Msg != WmMouseWheel)
            {
                return false;
            }

            // 按**指针底下**认领这一笔：消息发给谁取决于焦点，焦点不在表面上时按消息目标解析会认错人。
            var under = GetControlAtScreenPoint(Cursor.Position);
            var host = ResolveSurfaceHost(under) ?? ResolveSurfaceHost(m.HWnd);
            if (host is null)
            {
                return false;
            }

            var tree = ResolveTree(host);
            if (tree is null)
            {
                return false;
            }

            var delta = unchecked((short)((uint)m.WParam.ToInt64() >> 16));

            // 普通滚轮（或这家关掉了缩放）**整笔归适配器**：先路由，订阅方置 PreventDefault 就是「这一笔
            // 不滚」，由他走 Scroller 决定往哪滚；否则执行默认竖滚。两条都吞掉消息，平台一次都不滚画布。
            if (Control.ModifierKeys != Keys.Control || !GetState(host).ZoomEnabled)
            {
                if (!RouteWheel(host, tree, under ?? host, delta))
                {
                    GetState(host).Scroller?.ScrollBy(0d, delta);
                }

                m.Result = IntPtr.Zero;
                return true; // 吞掉消息：平台自己一次都不滚画布
            }

            // 缩放也要能被订阅者否决。滚轮在消息层就被这里接住、画布收不到它，所以路由只能在这一层补一次；
            // 否决即吞掉消息 —— 不缩放，也不让任何控件滚动。
            if (RouteWheel(host, tree, under ?? host, delta))
            {
                m.Result = IntPtr.Zero;
                return true;
            }

            // 滚轮向上（增量为正）放大：Scale 是折叠因子，放大要除以 1/1.1。
            var factor = delta > 0 ? 1 / 1.1 : 1.1;
            var next = Math.Max(0.1, Math.Min(10, tree.Layout.Scale.Horizontal * factor));
            var layout = tree.Layout;

            if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
            {
                var scrollOffset = ResolveScrollOffset(host, tree);
                var clientSize = ResolveClientSize(host);
                var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                    scrollOffset.Horizontal, scrollOffset.Vertical, clientSize.Width, clientSize.Height, layout);
                layout.CollapsePivot = new Anchor(wx, wy, 0);
                layout.Scale = new Scale(next, next);
                // 深度放大把负向内容折叠到 w/Scale、越过固定 NegativeOffset；先扩大覆盖（单调，只有正向内容时无事），下面的 PivotCenterScroll 与 Refresh 才会读到新的 ActualOffset。
                WorkflowSurfaceMath.EnsureNegativeCover(tree);
                var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, clientSize.Width, clientSize.Height);
                ApplyScrollOffset(host, tx, ty);
                Refresh(host);
            }
            else
            {
                layout.Scale = new Scale(next, next);
                // 世界原点缩放保持内容左上对齐：下游没人读新的 ActualOffset，所以把长大的覆盖（若有）显式经重绘路径推出去。
                if (WorkflowSurfaceMath.EnsureNegativeCover(tree))
                {
                    Refresh(host);
                }
            }

            m.Result = IntPtr.Zero;
            return true; // swallow the message: the target control never scrolls
        }

        private Control? ResolveSurfaceHost(IntPtr hwnd) => ResolveSurfaceHost(Control.FromHandle(hwnd));

        // 从命中控件沿父链认领表面：判据是**启用**着的表面，不是「开着缩放」的 —— 关掉缩放的宿主一样要收到滚轮汇报。
        private static Control? ResolveSurfaceHost(Control? hit)
        {
            var host = hit;
            while (host is not null)
            {
                if (States.TryGetValue(host, out var state) && state.IsEnabled)
                {
                    return host;
                }

                host = host.Parent;
            }

            return null;
        }

        internal static Control? GetControlAtScreenPoint(Point screenPoint)
        {
            var handle = WindowFromPoint(screenPoint);
            return handle == IntPtr.Zero ? null : Control.FromChildHandle(handle) ?? Control.FromHandle(handle);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
    }

    private static readonly ConditionalWeakTable<Control, SurfaceState> States = new();

    /// <summary>
    /// Gets whether the workflow surface behavior is enabled for the specified control.
    /// </summary>
    public static bool GetIsEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).IsEnabled;
    }

    /// <summary>
    /// Sets whether the workflow surface behavior is enabled for the specified control.
    /// </summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        if (state.IsEnabled == value)
        {
            return;
        }

        state.IsEnabled = value;

        if (value)
        {
            // 滚轮的汇报入口：消息过滤器在应用层看得见**每一笔**滚轮（消息发给谁取决于焦点，事件层的
            // 处理器在画布没焦点时一笔都收不到 —— 实测三格零到达）。开关跟着 IsEnabled 走。
            state.EnsureMessageFilter();

            // 宿主画布启用时自动编排 Win32 窗口样式，消除自绘画布与子窗口（节点卡片）重绘分离造成的闪烁/残影：画布窗口加 WS_CLIPCHILDREN，其顶层窗体加 WS_EX_COMPOSITED（DWM 合成整个窗体树）。宿主无需改动。
            NativeWindowStyleHelper.EnsureClipChildren(element);
            NativeWindowStyleHelper.EnsureComposited(element);
        }
        else
        {
            state.RemoveMessageFilter();
            ClearScroller(element);
        }
    }

    /// <summary>Gets whether Ctrl + mouse-wheel zoom is enabled for the specified surface control.</summary>
    public static bool GetZoomEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ZoomEnabled;
    }

    /// <summary>Sets whether Ctrl + mouse-wheel zoom is enabled for the specified surface control.</summary>
    public static void SetZoomEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        // 只记状态：接住 Ctrl+滚轮的是消息过滤器（它跟着 IsEnabled 挂）。滚轮在消息层就被过滤器吞掉，
        // 控件自己的 MouseWheel 一笔都收不到，所以这里不再另挂一条 —— 那只会是一份不走输入路由的重复缩放。
        GetState(element).ZoomEnabled = value;
    }

    /// <summary>Gets the scroll viewer the host handed over, when it has one.</summary>
    public static Control? GetScrollViewer(Control element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return GetState(element).ScrollViewer;
    }

    /// <summary>Hands the scroll viewer over to the behaviour — the object, not a control name to look up.</summary>
    public static void SetScrollViewer(Control element, Control? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        GetState(element).ScrollViewer = value;
        EnsureClipChildrenFor(element, value);
    }

    /// <summary>Gets the canvas the host handed over, when it has one.</summary>
    public static Control? GetCanvas(Control element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return GetState(element).Canvas;
    }

    /// <summary>Hands the canvas over to the behaviour — the object, not a control name to look up.</summary>
    public static void SetCanvas(Control element, Control? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        GetState(element).Canvas = value;
        EnsureClipChildrenFor(element, value);
    }

    /// <summary>Gets the grid decorator the host handed over, when it has one.</summary>
    public static Control? GetGridDecorator(Control element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return GetState(element).GridDecorator;
    }

    /// <summary>Hands the grid decorator over to the behaviour — the object, not a control name to look up.</summary>
    public static void SetGridDecorator(Control element, Control? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        GetState(element).GridDecorator = value;
        EnsureClipChildrenFor(element, value);
    }

    /// <summary>Gets the minimap overlay the host handed over, when it has one.</summary>
    public static Control? GetMinimapOverlay(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).MinimapOverlay;
    }

    /// <summary>
    /// Hands the minimap overlay over to the behaviour — the object, not a control name to look up. When it
    /// implements <see cref="IWorkflowMinimapOverlay"/>, <see cref="Refresh"/> pushes scroll, content offset,
    /// viewport, and tree values into it on every refresh cycle.
    /// </summary>
    public static void SetMinimapOverlay(Control element, Control? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).MinimapOverlay = value;
    }

    /// <summary>
    /// Gets the workflow tree bound to the surface host, if one was set via <see cref="SetWorkflowTree"/>.
    /// </summary>
    public static IWorkflowTreeViewModel? GetWorkflowTree(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).WorkflowTree;
    }

    /// <summary>
    /// Explicitly binds the workflow tree to the surface host so <see cref="Refresh"/> can push the
    /// visible viewport and decorator/minimap offsets. Mirrors setting the host <c>DataContext</c> in
    /// the XAML adapters.
    /// </summary>
    public static void SetWorkflowTree(Control element, IWorkflowTreeViewModel? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).WorkflowTree = value;
        RegisterScroller(element, value);
    }

    // 把画布滚动口挂到当前这棵树的路由上；换树/解绑时先摘掉自己挂的旧那一个（只摘自己的）。
    private static void RegisterScroller(Control host, IWorkflowTreeViewModel? tree)
    {
        ClearScroller(host);
        if (tree is null)
        {
            return;
        }

        var state = GetState(host);
        state.Scroller = new SurfaceScroller(host);
        state.ScrollerTree = tree;
        WorkflowInput.For(tree).Scroller = state.Scroller;
    }

    // 从挂着的树上摘下滚动口 —— 只在自己挂的那一个还挂着时摘，同一棵树上别的表面挂的留给它。
    private static void ClearScroller(Control host)
    {
        var state = GetState(host);
        if (state.ScrollerTree is { } tree && state.Scroller is { } scroller
            && ReferenceEquals(WorkflowInput.For(tree).Scroller, scroller))
        {
            WorkflowInput.For(tree).Scroller = null;
        }

        state.Scroller = null;
        state.ScrollerTree = null;
    }

    // 画布滚动的中立请求。收的是**滚轮增量**（正 = 远离用户），换算成这家自己的步长再平移画布 ──
    // 用的是本家自己那套平移（与缩略图拖拽同一条路），不是去动滚动容器的偏移。
    private sealed class SurfaceScroller : IWorkflowSurfaceScroller
    {
        // 一格滚轮是 WHEEL_DELTA（120）。本家没有原生滚动步长可复刻（PART_ScrollViewer 的 AutoScroll 是关的，
        // 视口由平移引擎拥有），所以取系统设置的滚轮行数 × 行高 16px —— 默认 3 行 = 48px，与 WPF 那边一致。
        private const double Notch = 120d;
        private const double LineHeight = 16d;

        private readonly Control host;

        public SurfaceScroller(Control host) => this.host = host;

        public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
        {
            var tree = ResolveTree(host);
            var current = ResolveScrollOffset(host, tree);
            var clientSize = ResolveClientSize(host);

            // WheelScrollLines 为 -1 是「按页滚」，与平台自己的解释一致。
            var lines = SystemInformation.MouseWheelScrollLines;
            var stepX = lines > 0 ? lines * LineHeight : clientSize.Width;
            var stepY = lines > 0 ? lines * LineHeight : clientSize.Height;

            // 正 = 远离用户 = 视口上移，所以滚动量是减。
            var dx = -(wheelDeltaX / Notch) * stepX;
            var dy = -(wheelDeltaY / Notch) * stepY;

            ApplyScrollOffset(host, current.Horizontal + dx, current.Vertical + dy);
        }
    }

    /// <summary>
    /// Requests the host to refresh its layout and redraw, mirroring other workflow surface adapters.
    /// In addition to <c>PerformLayout</c>/<c>Invalidate</c>, this pushes the current scroll/content
    /// offsets into any named <see cref="IWorkflowGridDecorator"/>/<see cref="IWorkflowMinimapOverlay"/>
    /// controls and updates the workflow tree's visible viewport from the host scroll position.
    /// </summary>
    public static void Refresh(Control host)
    {
        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        var state = GetState(host);
        var tree = ResolveTree(host);
        CaptureViewportRestore(tree, state);
        var scrollOffset = ResolveScrollOffset(host, tree, out var measuredScroll);
        var clientSize = ResolveClientSize(host);
        var contentOffset = tree?.Layout?.ActualOffset ?? new Offset();

        // 更新树的视口，让消费者（如空间虚拟化）观察到当前可见区；尽力而为，刷新周期里绝不抛异常。
        if (tree is not null && clientSize.Width > 0 && clientSize.Height > 0)
        {
            try
            {
                tree.GetHelper().Viewport = new Viewport(
                    WorkflowSurfaceMath.ToWorld(scrollOffset.Horizontal, contentOffset.Horizontal),
                    WorkflowSurfaceMath.ToWorld(scrollOffset.Vertical, contentOffset.Vertical),
                    clientSize.Width,
                    clientSize.Height);
            }
            catch
            {
                // 某些宿主上树 helper 可能不支持写视口；忽略。
            }

            // 持久化与上面交给视口相同的世界位置，画布位置才能熬过存/取。用 measuredScroll 把关：未测量的偏移本就来自 ViewportOffset 自身。
            if (measuredScroll)
            {
                tree.Layout.ViewportOffset = WorkflowSurfaceMath.ViewportOffsetFromScroll(
                    scrollOffset.Horizontal, scrollOffset.Vertical, tree.Layout);
            }
        }

        if (state.GridDecorator is IWorkflowGridDecorator decorator)
        {
            decorator.ScrollOffsetX = scrollOffset.Horizontal;
            decorator.ScrollOffsetY = scrollOffset.Vertical;
            decorator.ContentOffsetX = contentOffset.Horizontal;
            decorator.ContentOffsetY = contentOffset.Vertical;

            // 让虚拟化可见区修正与装饰器的浮动标尺带保持一致，标尺下方的节点才不会提前一个标尺厚度被剔除。
            tree?.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand);
        }

        if (state.MinimapOverlay is IWorkflowMinimapOverlay minimap)
        {
            minimap.ScrollOffsetX = scrollOffset.Horizontal;
            minimap.ScrollOffsetY = scrollOffset.Vertical;
            minimap.ContentOffsetX = contentOffset.Horizontal;
            minimap.ContentOffsetY = contentOffset.Vertical;
            minimap.ViewportWidth = clientSize.Width;
            minimap.ViewportHeight = clientSize.Height;
            minimap.WorkflowTree = tree;
        }

        // 把平移变换作为通知载体发布给宿主画布。
        WorkflowCanvasTransformBehavior.Apply(host, contentOffset);

        host.PerformLayout();

        // 异步失效：WM_PAINT 由消息循环合并。非交互场景（运行时状态刷新、滚动、集合变化）频繁调用 Refresh，总同步重绘会让自绘画布每帧重画并卡顿，所以默认保持异步。
        // 例外：宿主捕获鼠标时（画布平移/拖拽进行中）同步重绘 —— 否则高频鼠标消息一直推迟 WM_PAINT，节点旧位置与旧连线来不及擦掉、留下残影。
        // 节点拖拽的同步重绘由 WorkflowNodeDragBehavior 单独触发，这里不重复。
        host.Invalidate();
        if (host.Capture)
        {
            host.Update();
        }

        QueueViewportRestore(host, state);
    }

    // 树刚挂上来且不是上一棵：把它存档里的视口位置排进待恢复（世界 → 有效滚动）。
    // 必须在写 Viewport / ViewportOffset 之前 —— 那一步会拿宿主当前（还没滚过去的）位置把它覆盖掉。
    private static void CaptureViewportRestore(IWorkflowTreeViewModel? tree, SurfaceState state)
    {
        if (ReferenceEquals(tree, state.LastRestoreTree)) return;

        state.LastRestoreTree = tree;
        state.HasPendingRestore = false;

        if (tree is null || !WorkflowSurfaceMath.HasViewportRestore(tree.Layout)) return;

        var scroll = WorkflowSurfaceMath.ViewportRestoreScroll(tree.Layout);
        state.PendingRestoreX = scroll.Horizontal;
        state.PendingRestoreY = scroll.Vertical;
        state.HasPendingRestore = true;
    }

    // 布局稳定后再滚：挂树这一刻宿主往往还没创建句柄（没排版，可滚范围还是 0，滚了会被夹没）。
    // 句柄没创建时留着标记不清 —— HandleCreated / InitialSync 那两条路会再走一次 Refresh，那时才排。
    private static void QueueViewportRestore(Control host, SurfaceState state)
    {
        if (!state.HasPendingRestore || state.RestoreQueued || !host.IsHandleCreated) return;

        state.RestoreQueued = true;
        host.BeginInvoke(new Action(() =>
        {
            state.RestoreQueued = false;

            if (!state.HasPendingRestore || !state.IsEnabled) return;

            state.HasPendingRestore = false;
            ApplyScrollOffset(host, state.PendingRestoreX, state.PendingRestoreY);

            // 滚动落地后立刻把位置写回模型，免得控件与 Layout.ViewportOffset 各说各话。
            Refresh(host);
        }));
    }

    private static IWorkflowTreeViewModel? ResolveTree(Control host)
    {
        if (GetWorkflowTree(host) is { } explicitTree)
        {
            return explicitTree;
        }

        var current = host;
        while (current is not null)
        {
            IWorkflowTreeViewModel? tree = ResolveValue(current, "ViewModel") as IWorkflowTreeViewModel;
            tree ??= ResolveValue(current, "DataContext") as IWorkflowTreeViewModel;
            tree ??= ResolveValue(current, "BindingContext") as IWorkflowTreeViewModel;
            tree ??= current.Tag as IWorkflowTreeViewModel;
            if (tree is not null)
            {
                return tree;
            }

            current = current.Parent;
        }

        return null;
    }

    // 缩放这一手的路由：位置取光标在画布客户区的坐标（与表面自己那条滚轮路同一系），目标用与表面同一套
    // 解析。返回 true 表示订阅者否决了这一次缩放。
    private static bool RouteWheel(Control host, IWorkflowTreeViewModel tree, Control hit, int delta)
    {
        var canvas = ResolveCanvas(host) ?? host;
        var point = canvas.PointToClient(Cursor.Position);
        var anchor = new Anchor(point.X, point.Y, 0);
        var input = WorkflowInput.For(tree);
        var target = ResolveTarget(hit, tree, anchor.Horizontal, anchor.Vertical, input.HitRadius);
        var handle = new WorkflowEventHandle();

        input.Route(new Wf.PointerWheelEventArgs(anchor, Modifiers(), canvas, target, 0d, delta, handle));
        return handle.PreventDefault;
    }

    /// <summary>
    /// Routes one press that landed on a component view rather than on the surface's canvas.
    /// </summary>
    /// <param name="source">The control the press landed on — a node card or a slot view, or a control inside one.</param>
    /// <param name="target">The workflow component <paramref name="source"/> renders.</param>
    /// <param name="button">Which button went down.</param>
    /// <param name="clickCount">How many clicks this press completes.</param>
    /// <returns>The handle the route produced, or <see langword="null"/> when no surface or tree was found.</returns>
    /// <remarks>
    /// WinForms delivers a press to the enabled child control under the pointer, so the surface's own
    /// <c>MouseDown</c> never runs for a node card or a slot. Its gesture behaviours route the press here instead,
    /// with the component itself as the target — which is what makes the slot → node → tree chain reachable. A
    /// subscriber that sets <see cref="WorkflowEventHandle.PreventDefault"/> on the returned handle refuses this one
    /// press, exactly as it would on the surface.
    /// </remarks>
    internal static WorkflowEventHandle? RouteComponentPress(
        Control source, IWorkflowViewModel target, PlatformInput.MouseButtons button, int clickCount)
    {
        var host = FindSurfaceHost(source);
        if (host is null) return null;

        var tree = ResolveTree(host);
        if (tree is null) return null;

        var canvas = ResolveCanvas(host) ?? host;
        var point = canvas.PointToClient(Cursor.Position);
        var handle = new WorkflowEventHandle();

        WorkflowInput.For(tree).Route(new Wf.PointerPressedEventArgs(
            new Anchor(point.X, point.Y, 0), Modifiers(), canvas, target, ToButton(button), clickCount, handle));
        return handle;
    }

    // 指针底下是什么：先沿命中控件及其可视祖先找节点/插槽 —— 本家没有标记语言，视图把模型放在
    // Tag/ViewModel/DataContext 上 —— 找不到再回退到共享的曲线判定。只认连线的话，输入链里
    // slot → node → tree 那两级永远走不到。
    internal static IWorkflowViewModel? ResolveTarget(
        Control? hit, IWorkflowTreeViewModel tree, double x, double y, double radius)
    {
        for (var current = hit; current is not null; current = current.Parent)
        {
            var model = ResolveValue(current, "ViewModel")
                ?? ResolveValue(current, "DataContext")
                ?? ResolveValue(current, "BindingContext")
                ?? current.Tag;

            switch (model)
            {
                case IWorkflowNodeViewModel node:
                    return node;
                case IWorkflowSlotViewModel slot:
                    return slot;
            }
        }

        return tree.HitTestVisibleLinks(x, y, radius);
    }

    // 沿父链找启用着本行为的表面宿主 —— 与 zoom 过滤器那条 ResolveSurfaceHost 同一判据。
    private static Control? FindSurfaceHost(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (States.TryGetValue(current, out var state) && state.IsEnabled)
            {
                return current;
            }
        }

        return null;
    }

    // 表面上任何一次按下（画布、卡片、插槽、连线）都把键盘焦点收到画布 —— Ctrl+Z 这类树级按键要有人接才
    // 进得来。画布之外的按下（缩略图、HUD 按钮）与可编辑控件里的按下不动它：在那里按 Ctrl+Z 撤的是文字。
    private static void FocusSurfaceForPress()
    {
        var under = SurfaceState.GetControlAtScreenPoint(Cursor.Position);
        if (under is null)
        {
            return;
        }

        var host = FindSurfaceHost(under);
        if (host is null)
        {
            return;
        }

        var canvas = GetState(host).Canvas;
        if (canvas is null || !IsWithinCanvas(under, canvas) || IsInsideEditable(under))
        {
            return;
        }

        if (canvas.CanFocus)
        {
            canvas.Focus();
        }
    }

    // 这一笔是否落在表面上：沿父链走到画布。缩略图与 HUD 挂在宿主上、不在画布里，因此被排除。
    private static bool IsWithinCanvas(Control? control, Control canvas)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, canvas))
            {
                return true;
            }
        }

        return false;
    }

    // 落在可编辑控件里的按下，焦点归那个控件 —— 表面不该把它抢走。
    private static bool IsInsideEditable(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current is TextBoxBase)
            {
                return true;
            }
        }

        return false;
    }

    private static Wf.InputModifiers Modifiers()
    {
        var keys = Control.ModifierKeys;
        var modifiers = Wf.InputModifiers.None;
        if ((keys & Keys.Alt) != 0) modifiers |= Wf.InputModifiers.Alt;
        if ((keys & Keys.Control) != 0) modifiers |= Wf.InputModifiers.Control;
        if ((keys & Keys.Shift) != 0) modifiers |= Wf.InputModifiers.Shift;
        return modifiers;
    }

    private static Wf.MouseButton ToButton(PlatformInput.MouseButtons button) => button switch
    {
        PlatformInput.MouseButtons.Left => Wf.MouseButton.Left,
        PlatformInput.MouseButtons.Right => Wf.MouseButton.Right,
        PlatformInput.MouseButtons.Middle => Wf.MouseButton.Middle,
        PlatformInput.MouseButtons.XButton1 => Wf.MouseButton.XButton1,
        PlatformInput.MouseButtons.XButton2 => Wf.MouseButton.XButton2,
        _ => Wf.MouseButton.None,
    };

    private static Offset ResolveScrollOffset(Control host, IWorkflowTreeViewModel? tree)
        => ResolveScrollOffset(host, tree, out _);

    // 解析宿主的有效滚动位置。measured 表示它来自真实的平移量、而不是 ViewportOffset 那条兜底 ——
    // 只有量到的才允许写回：兜底返回的就是要写的那个值本身，写回去等于让它穿过一次下面注释警告过的那次相减。
    private static Offset ResolveScrollOffset(Control host, IWorkflowTreeViewModel? tree, out bool measured)
    {
        // 有效滚动 = 宿主世界原点平移的负值，让 WorldAtViewportCenter 看到的滚动空间与节点定位一致。节点视图位于 node.Anchor + pan（对单独平移内容的宿主再加 ActualOffset），
        // 绝不在 node.Anchor + ViewportOffset —— 退回 ViewportOffset 会把内容偏移减两次，抓取的枢轴每个滚轮格都漂。
        if (host is ScrollableControl scrollable && scrollable.AutoScroll)
        {
            // 完整示例宿主：节点平移 = _panOffset + AutoScrollPosition；滚动范围夹在 >= 0，枢轴只能在其内到达（越界被夹）。
            var pan = ResolvePanOffset(host) ?? new System.Drawing.Point();
            measured = true;
            return new Offset(-(pan.X + scrollable.AutoScrollPosition.X), -(pan.Y + scrollable.AutoScrollPosition.Y));
        }

        // 带符号平移宿主（模板/Trimmed 示例）：画布固定在视口上、节点视图定位在 node.Anchor + PanOffset，所以有效滚动 = -PanOffset。
        var signedPan = ResolvePanOffset(host);
        if (signedPan is not null)
        {
            measured = true;
            return new Offset(-signedPan.Value.X, -signedPan.Value.Y);
        }

        // 未暴露平移：退回持久化的视口偏移（世界坐标）。
        measured = false;
        return tree?.Layout?.ViewportOffset ?? new Offset();
    }

    /// <summary>
    /// Applies a ViewportCenter-zoom scroll (effective scroll space) back into the host's pan
    /// translate. Mirrors the capture path in <see cref="ResolveScrollOffset"/>: an AutoScroll host
    /// receives AutoScrollPosition (negative-signed getter), a signed-pan host receives the scroll
    /// through its own OnMinimapScrollRequested(sx, sy) → _panOffset = (-sx, -sy) + ApplyPan — the
    /// exact "put world point (sx, sy) at the viewport origin" contract the recenter needs. Going
    /// through the host keeps the private pan field, node positions, grid/minimap and viewport all
    /// consistent — writing the canvas PanOffset property directly would be clobbered by the
    /// deferred ApplyPan the Layout property change schedules.
    /// </summary>
    private static void ApplyScrollOffset(Control host, double x, double y)
    {
        if (host is ScrollableControl scrollable && scrollable.AutoScroll)
        {
            // WinForms 的 AutoScrollPosition setter 会取反参数（getter = −setter），而节点平移含平移偏移，所以要把有效滚动落到 (x, y)，setter 必须收到 (x + panOffset)。
            // 验证：getter scr = −(x + pan)，有效滚动 = −(pan + scr) = x。(x + pan) 的形状与示例自己的缩略图补偿一致（_panOffset = −sx − scroll → setter = sx + panOffset）。
            var pan = ResolvePanOffset(host) ?? new System.Drawing.Point();
            scrollable.AutoScrollPosition = new System.Drawing.Point((int)Math.Round(x + pan.X), (int)Math.Round(y + pan.Y));
            return;
        }

        // 带符号平移宿主：经它自己的缩略图滚动 handler 重新居中（与平移同一套 _panOffset = (-sx, -sy); ApplyPan()）。跳过完整示例（AutoScroll）—— 上面已处理 —— 以及任何 handler 会递归进消息过滤器的控件。
        // 反射查方法要**逐级基类**地找（DeclaredOnly）：私有成员不被派生类型继承，而宿主的运行时类型是派生类
        // （Trimmed demo 的 TreeView），只查 p.GetType() 找不到基类 WorkflowTreeView 上那个私有方法。
        var target = ResolveCanvas(host) ?? host;
        for (var p = target; p is not null; p = p.Parent)
        {
            for (var type = p.GetType(); type is not null; type = type.BaseType)
            {
                var method = type.GetMethod(
                    "OnMinimapScrollRequested",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly,
                    binder: null,
                    new[] { typeof(double), typeof(double) },
                    modifiers: null);
                if (method is null)
                {
                    continue;
                }

                try
                {
                    method.Invoke(p, new object[] { x, y });
                }
                catch
                {
                    // 尽力而为；之后那次刷新周期仍会重推视口。
                }

                return;
            }
        }
    }

    /// <summary>The canvas the host handed over, if any.</summary>
    private static Control? ResolveCanvas(Control host)
    {
        var state = GetState(host);
        return state.Canvas;
    }

    /// <summary>
    /// Reads the signed pan translate a host exposes as a <c>Point PanOffset</c> property (the
    /// template / Trimmed demo surface canvas) or keeps in a private <c>_panOffset</c> field (the
    /// full demo's self-drawn canvas). Same reflective lookup as the node-view template's
    /// <c>GetCanvasPanOffset</c> — the surface canvas is a private nested control, so the adapter
    /// cannot name its type. Returns <see langword="null"/> when no pan translate exists.
    /// </summary>
    private static System.Drawing.Point? ResolvePanOffset(Control host)
    {
        // 平移量在命名画布上（宿主树视图私有持有并在 ApplyPan 里推），所以反射从画布开始，不从宿主。
        var canvas = ResolveCanvas(host);
        for (var p = canvas ?? host; p is not null; p = p.Parent)
        {
            var property = p.GetType().GetProperty("PanOffset");
            if (property?.CanRead == true && property.PropertyType == typeof(System.Drawing.Point))
            {
                return (System.Drawing.Point)property.GetValue(p)!;
            }

            // 完整示例（自绘画布）：平移量在私有 _panOffset 字段里。
            var field = p.GetType().GetField(
                "_panOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.FieldType == typeof(System.Drawing.Point))
            {
                return (System.Drawing.Point)field.GetValue(p)!;
            }
        }

        return null;
    }

    private static System.Drawing.Size ResolveClientSize(Control host)
        => host.ClientSize.Width > 0 ? host.ClientSize : host.Size;

    // 给分层容器的窗口自动加 WS_CLIPCHILDREN：它们重绘时裁剪子区域，避免层叠闪烁盖住节点视图/连线。
    private static void EnsureClipChildrenFor(Control root, Control? control)
    {
        if (control is null)
        {
            return;
        }

        NativeWindowStyleHelper.EnsureClipChildren(control);
    }


    private static object? ResolveValue(Control control, string propertyName)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var property = control.GetType().GetProperty(propertyName, flags);
        if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return property.GetValue(control);
    }

    private static SurfaceState GetState(Control element)
        => States.GetValue(element, static _ => new SurfaceState());
}
