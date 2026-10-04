using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Blazor analogue of the XAML adapters' <c>WorkflowSurfaceBehavior</c> attached property.
/// Renders a scrollable workflow canvas and orchestrates scroll reporting, canvas panning
/// (middle mouse / space+left / left-drag on blank), viewport bookkeeping
/// (<see cref="IWorkflowTreeViewModelHelper.Viewport"/>), and pushes a
/// <see cref="SurfaceViewport"/> context into the optional grid-decorator and minimap fragments.
/// </summary>
public partial class WorkflowSurfaceBehavior : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>Gets or sets the workflow tree rendered by the surface.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets whether the surface behaviors (pan, scroll tracking) are enabled.</summary>
    [Parameter]
    public bool IsEnabled { get; set; }

    /// <summary>Gets or sets whether Ctrl + mouse-wheel zoom is enabled (wired via JS).</summary>
    [Parameter]
    public bool ZoomEnabled { get; set; }

    /// <summary>Gets or sets the scroll container element id.</summary>
    [Parameter]
    public string ScrollViewerId { get; set; } = "veloxdev-wf-scroll";

    /// <summary>Gets or sets the canvas element id.</summary>
    [Parameter]
    public string CanvasId { get; set; } = "veloxdev-wf-canvas";

    /// <summary>Gets or sets an optional ruler/grid-decorator fragment that receives the current <see cref="SurfaceViewport"/>.</summary>
    [Parameter]
    public RenderFragment<SurfaceViewport>? GridDecorator { get; set; }

    /// <summary>Gets or sets an optional minimap fragment that receives the current <see cref="SurfaceViewport"/>.</summary>
    [Parameter]
    public RenderFragment<SurfaceViewport>? Minimap { get; set; }

    /// <summary>Gets or sets the canvas content (nodes, links, slots). Receives the computed canvas size.</summary>
    [Parameter]
    public RenderFragment<SurfaceCanvas>? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the entries of the link context menu. The fragment receives the link the menu is
    /// about, so a host binds it directly in its buttons (for example
    /// <c>@onclick="() =&gt; link.DeleteCommand.Execute(null)"</c>) and writes no other wiring: the
    /// surface renders the chrome, positions the menu, opens and closes it, and reports both to the
    /// interaction hub.
    /// </summary>
    [Parameter]
    public RenderFragment<IWorkflowLinkViewModel>? LinkMenu { get; set; }

    /// <summary>Gets the link the open context menu is about, or <see langword="null"/> when none is open.</summary>
    public IWorkflowLinkViewModel? MenuLink { get; private set; }

    /// <summary>Gets or sets the canvas background color.</summary>
    [Parameter]
    public string Background { get; set; } = "#0B1120";

    /// <summary>Gets or sets the canvas grid line color.</summary>
    [Parameter]
    public string GridColor { get; set; } = "#2A2D2E";

    /// <summary>Gets or sets the canvas grid spacing in pixels.</summary>
    [Parameter]
    public double GridSpacing { get; set; } = 40;

    /// <summary>Gets or sets the canvas major grid line color.</summary>
    [Parameter]
    public string MajorGridColor { get; set; } = "#3A3D40";

    /// <summary>Gets or sets the number of minor cells between major grid lines.</summary>
    [Parameter]
    public int MajorLineEvery { get; set; } = 5;

    /// <summary>Gets or sets the canvas origin (world 0) axis line color.</summary>
    [Parameter]
    public string AxisColor { get; set; } = "#4D4D4D";

    /// <summary>Gets or sets the ruler band thickness reserved for the grid decorator overlay.</summary>
    [Parameter]
    public double RulerThickness { get; set; } = 28;

    private ElementReference _scroller;
    private ElementReference _canvasHost;
    private ElementReference _canvas;
    private ElementReference _grid;
    private ElementReference _axisX;
    private ElementReference _axisY;
    private ElementReference _content;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WorkflowSurfaceBehavior>? _dotNetRef;
    private IJSObjectReference? _handle;
    private IJSObjectReference? _wheelHandle;

    private double _scrollLeft;
    private double _scrollTop;
    private double _viewportW = 800;
    private double _viewportH = 600;
    private double _canvasW = 1600;
    private double _canvasH = 1200;
    private double _lastContentX;
    private double _lastContentY;

    // 左/上画布扩展的内容平移：画布元素向四个方向长大，内容包装元素按此偏移右/下移，世界（节点/插槽）坐标因此不动，新露出的区域出现在左/上。
    private double _offsetX;
    private double _offsetY;
    private SurfaceViewport _viewport = null!;

    // 树模型不会自动刷新视图：这里订上节点/连线集合、树自身、虚拟连线与每个节点的锚点/尺寸，
    // 变化时重渲染表面。ChildContent 随表面重渲染重新执行，模板的标记因此跟着刷新。
    private readonly List<IWorkflowNodeViewModel> _subscribedNodes = [];
    private IWorkflowTreeViewModel? _subscribedTreeModel;
    private INotifyPropertyChanged? _subscribedTreeNotifier;
    private INotifyPropertyChanged? _subscribedVirtualLink;

    // 连线右键菜单：条目由宿主以 LinkMenu 传入，接线全在这里 —— 订中枢、弹出、开合报回去。
    private LinkInteraction? _menuHub;
    private EventHandler<ContextMenuRequestedEventArgs>? _menuRequested;
    private EventHandler<ContextMenuDismissRequestedEventArgs>? _menuDismissed;
    private Anchor _menuPosition = new();
    private int _menuLeft;
    private int _menuTop;

    // 上一棵被挂上来的树（引用比较）。恢复只因「换了树」触发一次，之后的渲染不再把用户滚回去。
    private IWorkflowTreeViewModel? _lastRestoreTree;
    private bool _hasPendingRestore;
    private double _pendingScrollX;
    private double _pendingScrollY;

    /// <summary>
    /// Feeds one pointer event into the tree's <see cref="LinkInteraction"/> hub, converting the
    /// viewport coordinates the browser reports into the canvas-local space the link curves are
    /// published in.
    /// </summary>
    /// <param name="phase">What the pointer did.</param>
    /// <param name="clientX">Viewport x of the pointer, as reported by the browser.</param>
    /// <param name="clientY">Viewport y of the pointer, as reported by the browser.</param>
    /// <param name="button">Which button, for a press or a release; <see cref="PointerButtonKind.None"/> by default.</param>
    /// <remarks>
    /// A link view's <c>mouseenter</c>/<c>mouseleave</c> fires per DOM element, but the hub decides
    /// which link is topmost from the position, so the element it fired on is not passed through.
    /// While <see cref="LinkInteraction.IsSuspended"/> is set (a menu is open) the hub itself keeps
    /// the hovered link, so moving onto the menu does not clear the hover the menu acts on.
    /// </remarks>
    // 表面自己就是按键宿主（见 .razor 的 tabindex），生成的工程没有宿主代码，Delete 与 Escape 也有路由。删哪条仍由枢纽决定：这里只转发按键。
    private async Task OnSurfaceKeyDown(KeyboardEventArgs e)
    {
        // Escape 与菜单同层：菜单由表面弹，也由表面收，宿主不必再绑一次。
        if (e.Key == "Escape")
        {
            CloseLinkMenu();
            return;
        }

        if (e.Key is not ("Delete" or "Del") || Tree is not { } tree) return;

        var interaction = LinkInteraction.For(tree);
        if (interaction.HoveredLink is null) return;

        interaction.Publish(new KeyEvent(InputKey.Delete));
    }

    // 视口坐标是右键那一刻记下的；菜单相对视口定位，与画布坐标无关。
    private string MenuLeftCss => _menuLeft.ToString(CultureInfo.InvariantCulture);
    private string MenuTopCss => _menuTop.ToString(CultureInfo.InvariantCulture);

    // 右键落在表面上：屏幕坐标只有 DOM 事件知道（先记下），再把这次右键喂进枢纽 ——
    // 枢纽命中连线才报 ContextMenuRequested，菜单据此弹出；空白画布不给菜单。
    private async Task OnSurfaceContextMenu(MouseEventArgs e)
    {
        // 客户端坐标取整后写出去：整数字符串没有小数点，区域设置就碰不到它。
        _menuLeft = (int)Math.Round(e.ClientX);
        _menuTop = (int)Math.Round(e.ClientY);

        await ForwardPointerAsync(PointerPhase.Pressed, e.ClientX, e.ClientY, PointerButtonKind.Right);
    }

    // 换树才重接：按模型实例比对，同一棵树在重复的 OnParametersSet 里不再动订阅。
    private void SyncTreeSubscriptions()
    {
        if (ReferenceEquals(_subscribedTreeModel, Tree)) return;
        UnsubscribeTree();
        SubscribeTree(Tree);
    }

    private void SubscribeTree(IWorkflowTreeViewModel? tree)
    {
        if (tree is null) return;

        _subscribedTreeModel = tree;

        if (tree is INotifyPropertyChanged np)
        {
            _subscribedTreeNotifier = np;
            np.PropertyChanged += OnTreePropertyChanged;
        }

        tree.Nodes.CollectionChanged += OnNodesOrLinksChanged;
        tree.Links.CollectionChanged += OnNodesOrLinksChanged;

        // 虚拟连线自己发 PropertyChanged（Send/Receive/Reset 只改它、不改树），所以直接订它重画手势。
        if (tree.VirtualLink is INotifyPropertyChanged vp)
        {
            _subscribedVirtualLink = vp;
            vp.PropertyChanged += OnVirtualLinkPropertyChanged;
        }

        SubscribeNodeChanges(tree);
    }

    private void UnsubscribeTree()
    {
        if (_subscribedTreeNotifier is not null)
        {
            _subscribedTreeNotifier.PropertyChanged -= OnTreePropertyChanged;
            _subscribedTreeNotifier = null;
        }

        if (_subscribedTreeModel is not null)
        {
            _subscribedTreeModel.Nodes.CollectionChanged -= OnNodesOrLinksChanged;
            _subscribedTreeModel.Links.CollectionChanged -= OnNodesOrLinksChanged;
            _subscribedTreeModel = null;
        }

        if (_subscribedVirtualLink is not null)
        {
            _subscribedVirtualLink.PropertyChanged -= OnVirtualLinkPropertyChanged;
            _subscribedVirtualLink = null;
        }

        UnsubscribeNodeChanges();
    }

    private void SubscribeNodeChanges(IWorkflowTreeViewModel tree)
    {
        foreach (var node in tree.Nodes)
        {
            if (node is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += OnNodePropertyChanged;
                _subscribedNodes.Add(node);
            }
        }
    }

    private void UnsubscribeNodeChanges()
    {
        foreach (var node in _subscribedNodes)
        {
            if (node is INotifyPropertyChanged npc)
                npc.PropertyChanged -= OnNodePropertyChanged;
        }
        _subscribedNodes.Clear();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 拖拽中的节点位置与连线实时更新（Anchor/Size 变化）。缩放时表面已用 JS 同步盖上折叠后的
        // 节点几何与连线（applyZoomSurface），这里若重渲染整棵树，会用往返回来的旧值重建节点/连线层，
        // 缩放中途闪一下。
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            if (WorkflowGeometryScope.IsZooming) return;
            InvokeAsync(StateHasChanged);
        }
    }

    private void OnNodesOrLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 节点增删会改变逐节点订阅集合；两者都喂给池化层。
        UnsubscribeNodeChanges();
        if (_subscribedTreeModel is not null) SubscribeNodeChanges(_subscribedTreeModel);
        InvokeAsync(StateHasChanged);
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnVirtualLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    // 订中枢：右键请求与「菜单指着的那条线离树」两相都在这。枢纽按树取用（Core 缓存），
    // 换树就换实例，所以按实例比对重新接。
    private void WireLinkMenu()
    {
        var hub = Tree is { } tree ? LinkInteraction.For(tree) : null;
        if (ReferenceEquals(hub, _menuHub)) return;

        if (_menuHub is not null)
        {
            UnsubscribeMenuHub();
            // 菜单还开着就换树：把 Closed 报回旧枢纽，它的挂起状态不会留在那儿。
            if (MenuLink is not null)
            {
                _menuHub.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, MenuLink));
                MenuLink = null;
            }
        }

        _menuHub = hub;
        if (hub is null) return;

        _menuRequested = (_, e) => ShowLinkMenu(e);
        _menuDismissed = (_, e) =>
        {
            if (!ReferenceEquals(MenuLink, e.Link)) return;
            CloseLinkMenu();
        };
        hub.ContextMenuRequested += _menuRequested;
        hub.ContextMenuDismissRequested += _menuDismissed;
    }

    private void UnsubscribeMenuHub()
    {
        if (_menuHub is null) return;
        if (_menuRequested is not null) _menuHub.ContextMenuRequested -= _menuRequested;
        if (_menuDismissed is not null) _menuHub.ContextMenuDismissRequested -= _menuDismissed;
        _menuRequested = null;
        _menuDismissed = null;
    }

    // 命中连线且宿主真的给了条目才开菜单：空白处枢纽也会报一次（Link 为 null），在这里挡掉。
    private void ShowLinkMenu(ContextMenuRequestedEventArgs e)
    {
        if (e.Link is null || LinkMenu is null) return;

        MenuLink = e.Link;
        _menuPosition = e.Position;
        // 报回枢纽：菜单在屏期间挂起悬停，指针移到菜单上不会清掉这次选中的连线。
        _menuHub?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, e.Position, e.Link));
        _ = InvokeAsync(StateHasChanged);
    }

    // 收起照常报 Closed，挂起随之放开。点条目、点背景、按 Escape 都走这里。
    private void CloseLinkMenu()
    {
        if (MenuLink is null) return;

        var link = MenuLink;
        MenuLink = null;
        _menuHub?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, link));
        _ = InvokeAsync(StateHasChanged);
    }

    // 点面板里任何一处都收起菜单 —— 与平台原生的右键菜单一致：条目一击即散。
    private void OnMenuPanelClick() => CloseLinkMenu();

    public async Task ForwardPointerAsync(PointerPhase phase, double clientX, double clientY, PointerButtonKind button = PointerButtonKind.None)
    {
        // 枢纽按树取用（Core 只保留一处）：本家不持有实例，换树自然换枢纽
        if (Tree is not { } tree)
        {
            return;
        }

        var interaction = LinkInteraction.For(tree);

        if (await ToCanvasLocalAsync(clientX, clientY) is not { } local)
        {
            return;
        }

        interaction.Publish(new PointerEvent(phase, new Anchor(local[0], local[1], 0), button));

        // 悬停到连线上就把焦点收到表面根：Delete 才有路由，而「悬停（不点）就能删」是契约。
        // preventScroll 是本家对那一次「焦点把画布卷进视口」的防护 —— 不用它，鼠标碰到线画布就跳一段。
        if (interaction.HoveredLink is not null)
        {
            await _surfaceRoot.FocusAsync(preventScroll: true);
        }
    }

    // 视口坐标 → canvas-local：容器是纯平移，所以换算整个交给 JS（与槽口锚点测量同一公式）。
    // 模块未加载（IsEnabled 关掉）时没有可转发的位置，直接放弃。
    private async Task<double[]?> ToCanvasLocalAsync(double clientX, double clientY)
    {
        if (_module is null || string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            return null;
        }

        try
        {
            return await _module.InvokeAsync<double[]?>("toCanvasLocal", ScrollViewerId, clientX, clientY);
        }
        catch
        {
            return null;
        }
    }

    // 把最新视口快照广播给轻量覆盖层消费者（网格装饰器），它们无需拖着节点/连线内容子树就能重渲染。见 SurfaceViewportFeed。
    private readonly SurfaceViewportFeed _feed = new();

    // 表面的根元素（见 .razor 的 tabindex）：悬停到连线上时把焦点收到它，Delete 才有路由
    private ElementReference _surfaceRoot;

    private double ContentWidth => Math.Max(1, _canvasW - _offsetX);
    private double ContentHeight => Math.Max(1, _canvasH - _offsetY);

    /// <summary>
    /// Solid canvas background plus the grid CSS variables. The grid itself is a separate
    /// JS-positioned layer (see veloxdev.workflow.js), so the canvas carries no grid gradients and
    /// no size — the canvas host owns the size and is managed only by JS. Blazor re-renders of this
    /// style string therefore can never disturb the grid or shrink the canvas.
    /// </summary>
    private string CanvasBackgroundStyle
    {
        get
        {
            var spacing = Math.Max(8, GridSpacing);
            // JS 网格绘制器会从这个自定义属性读回网格间距，所以它必须可解析 —— 固定不变、用 '.'，与区域设置无关。
            return $"background-color:{Background};" +
                   $"--veloxdev-gs:{spacing.ToString("0.#", CultureInfo.InvariantCulture)}px;" +
                   $"--veloxdev-gc:{GridColor};--veloxdev-mgc:{MajorGridColor};--veloxdev-ac:{AxisColor};";
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // 预留标尺带：世界 0 位于内容边界（标尺右/下方），网格线与标尺刻度因此和节点锚点对齐。只增不减，与 JS 侧经 OnSurfaceScroll 上报的边界扩展一致（向左/上平移只增大偏移）。
        _offsetX = Math.Max(_offsetX, RulerThickness);
        _offsetY = Math.Max(_offsetY, RulerThickness);

        SyncTreeSubscriptions();
        WireLinkMenu();
        CaptureViewportRestore();

        if (Tree is not null)
        {
            var (w, h) = ComputeCanvasSize();
            _canvasW = Math.Max(_canvasW, w + _offsetX);
            _canvasH = Math.Max(_canvasH, h + _offsetY);
            _viewport = BuildViewport();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender && IsEnabled)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/VeloxDev.Razor/veloxdev.workflow.js");
            _dotNetRef = DotNetObjectReference.Create(this);
            var layout = Tree?.Layout;
            var contentX = layout?.ActualOffset.Horizontal ?? 0;
            var contentY = layout?.ActualOffset.Vertical ?? 0;
            // 恢复随初始表面一起进来，而不是后续调用，所以第一次上报就带着已保存位置而非原点。
            _handle = await _module.InvokeAsync<IJSObjectReference>("initSurface",
                _scroller, _canvasHost, _dotNetRef, _canvasW, _canvasH, contentX, contentY, _offsetX, _offsetY,
                _hasPendingRestore ? _pendingScrollX : 0, _hasPendingRestore ? _pendingScrollY : 0);
            _hasPendingRestore = false;

            if (ZoomEnabled)
            {
                _wheelHandle = await _module.InvokeAsync<IJSObjectReference>("initWheelZoom", _scroller, _dotNetRef);
            }
        }
        else if (_hasPendingRestore && IsEnabled && _module is not null && !string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            // 表面已激活后才换进来的树：滚动归 JS，所以恢复是一次 JS 调用。无需延迟 —— 模块已初始化、DOM 已布局。
            _hasPendingRestore = false;
            await _module.InvokeVoidAsync("scrollToPosition", ScrollViewerId, _pendingScrollX, _pendingScrollY);
        }
    }

    /// <summary>
    /// JS wheel callback. The argument is the signed wheel delta-y accumulated by the coalescing
    /// wheel handler: a human fast-flick sends several wheel events inside one SignalR round-trip, so
    /// the JS sums their deltas and we apply the NET count of notches here as compounding 1.1× steps
    /// in a SINGLE call. Each step captures the same world pivot and collapses nodes about it, but
    /// only the FINAL state is pushed to the DOM (one atomic <c>applyZoomSurface</c>), so a burst can
    /// never paint an intermediate stale frame and no notches are lost. Positive delta = zoom in.
    /// </summary>
    [JSInvokable]
    public async Task OnWheelZoom(int wheelDelta, double scrollX, double scrollY, double viewportW, double viewportH, double reachW, double reachH)
    {
        if (Tree is null)
        {
            return;
        }

        var layout = Tree.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
        {
            // 一次缩放事务：激活期间所有逐节点几何写入者（包装元素 SyncPosition、尺寸重渲染、树重渲染）让位 —— 下面的原子 applyZoomSurface 是该手势唯一的几何权威，浏览器不会出现中间帧把折叠节点画在旧的平移/滚动上（缩放闪烁）。
            using var _zoomScope = WorkflowGeometryScope.Zoom();

            // 整段连发只捕获一次视口中心下方的世界点 —— 滚动状态在唯一一次最终应用前不变，同一枢轴在每个复合步都成立。
            // scrollX/Y、viewportW/H、reachW/H 由 JS 滚轮处理器在连发开始时从 DOM 实时捕获（eff = DOM 滚动 − JS 边界预留）。
            // 快速第二次连发因此绕视口正下方的精确世界点缩放，绝不靠上次上报的滚动 —— 它一个往返的滞后曾把偏心偏移当成真相烘进去（深度缩放不可恢复的漂移）。
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(scrollX, scrollY, viewportW, viewportH, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);

            var notches = Math.Abs(wheelDelta) / 120d;
            var count = (int)Math.Max(1, Math.Round(notches));
            // 滚轮向上到达时 wheelDelta 为正（JS 处理器取反浏览器的 deltaY）且为放大：Scale 是折叠因子，放大要除以 1/1.1。
            var factor = wheelDelta > 0 ? 1 / 1.1 : 1.1;

            for (var i = 0; i < count; i++)
            {
                var next = Math.Max(0.1, Math.Min(10, layout.Scale.Horizontal * factor));
                layout.Scale = new Scale(next, next);
            }

            // 深度放大把负向内容折叠到 w/Scale、越过固定 NegativeOffset，把左/上内容推出可达的 DOM 宿主。
            // 现在扩大覆盖（单调，只有正向内容时无事），下面的 contentW/H、夹取与原子 applyZoomSurface 就会同帧读到新的 ActualOffset/ActualSize。
            WorkflowSurfaceMath.EnsureNegativeCover(Tree);

            // 比例低于 1 时画布内容随放大自动延伸（ActualSize = world / scale），夹取还可能增大 NegativeOffset（内容右/下移）。把新偏移与新范围连同下面的滚动原子地推给 DOM；先照 XAML 几家那样按变化后的模型范围算滚动。
            // 内容宽 = 模型 ActualSize（连线层），但不小于 DOM 宿主当前可达内容（reachW，由 JS 滚轮处理器实时读），这样边缘平移扩过的宿主不会被夹得比用户已滚到的还短。夹取上限是有效滚动范围（内容 − 视口），与我们扩展后 JS 宿主将暴露的一致。
            var contentW = Math.Max(1, Math.Max(layout.ActualSize.Width, reachW));
            var contentH = Math.Max(1, Math.Max(layout.ActualSize.Height, reachH));
            var (tx0, ty0) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, viewportW, viewportH);
            _ = WorkflowSurfaceMath.ClampScrollOffset(tx0, Math.Max(0, contentW - viewportW), layout, horizontal: true);
            _ = WorkflowSurfaceMath.ClampScrollOffset(ty0, Math.Max(0, contentH - viewportH), layout, horizontal: false);

            // 夹取可能增大了 NegativeOffset、移动了内容 —— 按新偏移/比例/范围重推滚动，枢轴才正好落在视口中心（第一趟 PivotCenterScroll 用的是夹取前的偏移）。
            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, viewportW, viewportH);
            tx = Math.Max(0, tx);
            ty = Math.Max(0, ty);

            // 一次原子 JS 步骤：把内容重平移到新 ActualOffset、扩展宿主让滚动范围覆盖（可能自动延伸的）模型内容，再滚动 —— 全在一个同步块里，浏览器按一帧绘制，世界不会出现「旧平移 + 新滚动」的中间帧（旧的左右闪烁）。生效空间长度：JS 会补回自己的边界预留。
            // 节点几何并入同一原子块：Scale 改变后每个节点折叠后的 Anchor/Size getter（world / scale）已正确，把它们编组过去，JS 同步重摆现有池化包装元素、并持续重申（surfaceZoomState 落定循环）直到异步的 .NET 逐节点渲染收敛 —— 陈旧渲染不会画出哪怕一帧旧折叠值。
            // 等待这一次原子应用，DOM（平移 + 宿主扩展 + 滚动 + 节点/连线几何）在本次连发返回前完全盖好，下一次连发的实时 DOM 读取因此是落定的最终态，绝不是半应用的中间态。
            if (_module is not null && !string.IsNullOrWhiteSpace(ScrollViewerId))
            {
                await _module.InvokeVoidAsync("applyZoomSurface",
                    ScrollViewerId,
                    layout.ActualOffset.Horizontal, layout.ActualOffset.Vertical,
                    contentW, contentH,
                    tx, ty,
                    NodeZoomGeometry());
            }
        }
        else
        {
            var notches = Math.Abs(wheelDelta) / 120d;
            var count = (int)Math.Max(1, Math.Round(notches));
            // 滚轮向上（wheelDelta 为正）放大：Scale 是折叠因子，放大要除以 1/1.1。
            var factor = wheelDelta > 0 ? 1 / 1.1 : 1.1;
            for (var i = 0; i < count; i++)
            {
                var next = Math.Max(0.1, Math.Min(10, layout.Scale.Horizontal * factor));
                layout.Scale = new Scale(next, next);
            }
        }
    }

    /// <summary>
    /// Marshals each node's collapsed geometry (world / scale) so JS can reposition the pooled
    /// wrappers synchronously inside <c>applyZoomSurface</c> — the same browser frame as the scroll.
    /// The node's <c>Anchor</c>/<c>Size</c> getters already return collapsed (post-scale) values once
    /// <see cref="CanvasLayout.Scale"/> is set, so this needs no per-node scale bookkeeping here.
    /// Marshaled as a <c>string[][]</c> (JS interop handles double[] cleanly but the wrapper contract
    /// is stringly-typed like the slot-layout batches); the target element is resolved by the
    /// <c>data-veloxdev-node-id</c> each wrapper renders, so no DOM-order assumption is needed.
    /// </summary>
    private string[][]? NodeZoomGeometry()
    {
        if (Tree?.Nodes is null) return null;

        var batch = new List<string[]>(Tree.Nodes.Count);
        foreach (var node in Tree.Nodes)
        {
            if (node is null) continue;
            var id = WorkflowRuntimeIds.Get(node);
            var anchor = node.Anchor;
            var size = node.Size;
            if (size.Width <= 0d || size.Height <= 0d) continue;
            batch.Add(
            [
                id,
                anchor.Horizontal.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                anchor.Vertical.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                size.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                size.Height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ]);
        }

        return batch.Count == 0 ? null : batch.ToArray();
    }

    [JSInvokable]
    public void OnSurfaceScroll(double scrollLeft, double scrollTop, double viewportW, double viewportH, double canvasW, double canvasH, double offsetX, double offsetY)
    {
        _scrollLeft = scrollLeft;
        _scrollTop = scrollTop;
        _viewportW = Math.Max(1, viewportW);
        _viewportH = Math.Max(1, viewportH);

        // 让 .NET 侧画布尺寸与内容平移跟 JS 侧自动扩展（只增）保持同步，后续重渲染不会把已在边缘扩展过的画布缩回去，也不会在用户平移中重置左/上偏移。
        var grew = false;
        if (canvasW > _canvasW)
        {
            _canvasW = canvasW;
            grew = true;
        }

        if (canvasH > _canvasH)
        {
            _canvasH = canvasH;
            grew = true;
        }

        if (offsetX > _offsetX)
        {
            _offsetX = offsetX;
            grew = true;
        }

        if (offsetY > _offsetY)
        {
            _offsetY = offsetY;
            grew = true;
        }

        if (Tree is not null)
        {
            var layout = Tree.Layout;
            var contentX = layout?.ActualOffset.Horizontal ?? 0;
            var contentY = layout?.ActualOffset.Vertical ?? 0;
            // 规范（上报）坐标系 —— 与 XAML 几家一致：预留标尺内缩与左/上越界增长都只是视觉平移，所以这里上报的 world-left 是 scroll − (ActualOffset + 越界量)。装饰器为刻度再加回标尺预留。
            var effX = contentX + Math.Max(0, _offsetX - RulerThickness);
            var effY = contentY + Math.Max(0, _offsetY - RulerThickness);
            var viewportX = WorkflowSurfaceMath.ToWorld(scrollLeft, effX);
            var viewportY = WorkflowSurfaceMath.ToWorld(scrollTop, effY);

            // 让虚拟化可见区修正与预留标尺带保持一致，标尺下方的节点才不会提前一个标尺厚度被剔除。
            Tree.SetVirtualizeInset(left: RulerThickness, top: RulerThickness);
            try
            {
                Tree.GetHelper().Viewport = new Viewport(viewportX, viewportY, _viewportW, _viewportH);
            }
            catch
            {
                // 尽力而为的视口记账；有些树可能不支持。
            }

            // 持久化视口位置（世界坐标）以熬过序列化往返 —— 与 XAML 几家 WorkflowSurfaceBehavior.UpdateVisibleRegion 一致。
            if (layout is not null)
            {
                layout.ViewportOffset = new Offset(viewportX, viewportY);
            }

            _viewport = BuildViewport();

            // 轻量覆盖层（网格装饰器）经 feed 更新，纯滚动不动节点/连线内容子树。画布宿主/网格/坐标轴归 JS，网格不再依赖重渲染。只有画布增长（边缘扩展）会改变连线层尺寸（SurfaceCanvas 上下文），所以只有那时才重渲染整个表面。
            _feed.Publish(_viewport);
            // 缩略图画在节点/世界内容之上，所以必须用物理可见世界矩形（含边缘平移）—— 而不是规范（排除标尺）的那个。
            var physX = WorkflowSurfaceMath.ToWorld(scrollLeft, _offsetX + contentX);
            var physY = WorkflowSurfaceMath.ToWorld(scrollTop, _offsetY + contentY);
            PushMinimapViewport(physX, physY);
            PushSurfaceLayout(contentX, contentY);
            if (grew)
            {
                InvokeAsync(StateHasChanged);
            }
        }
    }

    /// <summary>
    /// Pushes the current viewport world rect to the minimap's JS, which moves the viewport-block
    /// indicator directly (no .NET re-render). No-op when no minimap has registered that scroller id.
    /// </summary>
    private void PushMinimapViewport(double worldX, double worldY)
    {
        if (_module is not null && !string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            _ = _module.InvokeVoidAsync("setMinimapViewport",
                ScrollViewerId, worldX, worldY, _viewportW, _viewportH);
        }
    }

    /// <summary>
    /// Pushes the content translate (layout.ActualOffset) to JS so the grid/axis layers can align
    /// with world 0. The JS already tracks the edge-expansion offset (offsets.x/y); this supplies
    /// the layout offset on top. Only pushed when it changes (rare).
    /// </summary>
    private void PushSurfaceLayout(double contentX, double contentY)
    {
        if (_module is not null
            && !string.IsNullOrWhiteSpace(ScrollViewerId)
            && (Math.Abs(contentX - _lastContentX) > double.Epsilon
                || Math.Abs(contentY - _lastContentY) > double.Epsilon))
        {
            _lastContentX = contentX;
            _lastContentY = contentY;
            _ = _module.InvokeVoidAsync("setSurfaceLayout", ScrollViewerId, contentX, contentY);
        }
    }

    private (double W, double H) ComputeCanvasSize()
    {
        var layout = Tree?.Layout;
        double w = layout?.ActualSize.Width ?? 0;
        double h = layout?.ActualSize.Height ?? 0;
        double maxX = 0, maxY = 0;

        if (Tree?.Nodes is not null)
        {
            foreach (var node in Tree.Nodes)
            {
                maxX = Math.Max(maxX, node.Anchor.Horizontal + node.Size.Width);
                maxY = Math.Max(maxY, node.Anchor.Vertical + node.Size.Height);
            }
        }

        w = Math.Max(w, Math.Max(maxX + 200, _viewportW + 600));
        h = Math.Max(h, Math.Max(maxY + 200, _viewportH + 600));
        return (w, h);
    }

    private SurfaceViewport BuildViewport()
        => new(
            Tree ?? throw new InvalidOperationException("WorkflowSurfaceBehavior requires a Tree."),
            _scrollLeft,
            _scrollTop,
            _viewportW,
            _viewportH,
            EffectiveContentX,
            EffectiveContentY);

    /// <summary>Effective world origin in the canonical frame: ActualOffset + any left/top overscroll
    /// growth beyond the reserved ruler (which is a visual translate, like the XAML adapters).</summary>
    private double EffectiveContentX => (Tree?.Layout?.ActualOffset.Horizontal ?? 0) + Math.Max(0, _offsetX - RulerThickness);
    private double EffectiveContentY => (Tree?.Layout?.ActualOffset.Vertical ?? 0) + Math.Max(0, _offsetY - RulerThickness);

    // 树刚换过且不是上一棵：把存档视口换算成本家的滚动目标。
    // 本家的滚动空间比别的家多一段「超出 ruler 预留的平移」（见 OnSurfaceScroll 的 effX/effY），
    // 恢复必须加上同一个量，否则位置会差出 ruler 那一段。
    private void CaptureViewportRestore()
    {
        if (ReferenceEquals(Tree, _lastRestoreTree)) return;

        _lastRestoreTree = Tree;
        _hasPendingRestore = false;

        if (Tree is null || !WorkflowSurfaceMath.HasViewportRestore(Tree.Layout)) return;

        var layout = Tree.Layout;
        _pendingScrollX = layout.ViewportOffset.Horizontal + layout.ActualOffset.Horizontal
                          + Math.Max(0, _offsetX - RulerThickness);
        _pendingScrollY = layout.ViewportOffset.Vertical + layout.ActualOffset.Vertical
                          + Math.Max(0, _offsetY - RulerThickness);
        _hasPendingRestore = true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // 先摘模型订阅，拆 JS 句柄期间不再有回调进来要求重渲染。
        UnsubscribeTree();

        // 菜单还开着就收尾：把 Closed 报回去，旧枢纽的挂起状态不会留在那儿。
        if (MenuLink is not null)
        {
            _menuHub?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, MenuLink));
            MenuLink = null;
        }

        UnsubscribeMenuHub();
        _menuHub = null;

        if (_handle is not null)
        {
            try
            {
                await _handle.InvokeVoidAsync("dispose");
            }
            catch
            {
            }

            try
            {
                await _handle.DisposeAsync();
            }
            catch
            {
            }
        }

        if (_wheelHandle is not null)
        {
            try
            {
                await _wheelHandle.InvokeVoidAsync("dispose");
            }
            catch
            {
            }

            try
            {
                await _wheelHandle.DisposeAsync();
            }
            catch
            {
            }
        }

        _dotNetRef?.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch
            {
            }
        }
    }
}
