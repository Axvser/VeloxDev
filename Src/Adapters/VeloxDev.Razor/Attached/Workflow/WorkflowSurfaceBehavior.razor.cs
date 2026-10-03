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

    // Content translate for left/top canvas expansion. The canvas element grows in all four
    // directions; the content wrapper is shifted right/down by this offset so world (node/slot)
    // coordinates stay put while the newly revealed area appears to the left/top.
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
    // The surface itself is the key host (see the .razor tabindex), so Delete and Escape have a route in a
    // generated project with no host code. The hub still decides which link: this only forwards the key.
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

    // Broadcasts the latest viewport snapshot to cheap overlay consumers (grid decorator) so they
    // can re-render without dragging the node/link content subtree along. See SurfaceViewportFeed.
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
            // The grid spacing is read back out of this custom property by the JS grid painter, so it has to
            // stay parseable — invariant, with a '.', whatever the culture.
            return $"background-color:{Background};" +
                   $"--veloxdev-gs:{spacing.ToString("0.#", CultureInfo.InvariantCulture)}px;" +
                   $"--veloxdev-gc:{GridColor};--veloxdev-mgc:{MajorGridColor};--veloxdev-ac:{AxisColor};";
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // Reserve the ruler band: world 0 sits at the content boundary (right/below the ruler),
        // so grid lines and ruler ticks align with node anchors. Grow-only, mirroring the JS-side
        // edge expansion reported via OnSurfaceScroll (a pan left/up only grows the offset).
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
            // The restore rides in on the initial surface rather than a follow-up call, so the very first
            // report already carries the saved position instead of the origin.
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
            // A tree swapped in after the surface was already live: scroll is JS-owned, so the restore is
            // a JS call. No delay needed — the module is initialized and the DOM is laid out by now.
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
            // One zoom transaction: while active, every per-node geometry writer (wrapper SyncPosition,
            // size re-renders, tree re-renders) stands down — the atomic applyZoomSurface below is the
            // sole geometry authority for the gesture, so no intermediate browser frame can paint
            // collapsed nodes against the old translate/scroll (the zoom flicker).
            using var _zoomScope = WorkflowGeometryScope.Zoom();

            // Capture the world point under the viewport center ONCE for the whole burst — the scroll
            // state is unchanged until the single final apply, so the same pivot stays valid across
            // every compounding step.
            // scrollX/Y, viewportW/H and reachW/H are captured LIVE from the DOM by the JS wheel handler
            // at burst start (eff = DOM scroll − the JS edge reserve). A fast second burst therefore
            // zooms about the exact world point under the viewport right now — never the last REPORTED
            // scroll, whose one-round-trip lag used to bake an off-center offset in as ground truth (the
            // deep-zoom non-recovering drift).
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(scrollX, scrollY, viewportW, viewportH, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);

            var notches = Math.Abs(wheelDelta) / 120d;
            var count = (int)Math.Max(1, Math.Round(notches));
            // Wheel up arrives as positive wheelDelta (the JS handler negates the browser's deltaY)
            // and zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
            var factor = wheelDelta > 0 ? 1 / 1.1 : 1.1;

            for (var i = 0; i < count; i++)
            {
                var next = Math.Max(0.1, Math.Min(10, layout.Scale.Horizontal * factor));
                layout.Scale = new Scale(next, next);
            }

            // Deep zoom-in collapses negative-world content to w/Scale past the fixed NegativeOffset,
            // pushing left/top content outside the reachable DOM host. Grow the cover now (monotonic,
            // no-op for positive-only content) so the contentW/H, clamps and the atomic
            // applyZoomSurface below all read the NEW ActualOffset/ActualSize in the same frame.
            WorkflowSurfaceMath.EnsureNegativeCover(Tree);

            // The canvas content auto-extends on zoom-in below scale 1 (ActualSize = world / scale),
            // and clamping may grow NegativeOffset (content moves right/down). Push the new offset and
            // the new extent to the DOM atomically with the scroll below; first compute the scroll
            // against the post-change model extent exactly like the XAML adapters.
            //
            // Content width = the model ActualSize (the links layer) but at least the DOM host's
            // currently-reachable content (reachW, read live by the JS wheel handler) so an edge-pan-
            // expanded host is never clamped shorter than what the user already scrolled to. The clamp
            // max is the effective scroll extent (content − viewport), matching what the JS host will
            // expose after we grow it.
            var contentW = Math.Max(1, Math.Max(layout.ActualSize.Width, reachW));
            var contentH = Math.Max(1, Math.Max(layout.ActualSize.Height, reachH));
            var (tx0, ty0) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, viewportW, viewportH);
            _ = WorkflowSurfaceMath.ClampScrollOffset(tx0, Math.Max(0, contentW - viewportW), layout, horizontal: true);
            _ = WorkflowSurfaceMath.ClampScrollOffset(ty0, Math.Max(0, contentH - viewportH), layout, horizontal: false);

            // The clamp may have grown NegativeOffset, which moved the content — re-derive the scroll
            // from the NEW offset/scale/extent so the pivot lands exactly under the viewport center
            // (first-pass PivotCenterScroll used the pre-clamp offset).
            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, viewportW, viewportH);
            tx = Math.Max(0, tx);
            ty = Math.Max(0, ty);

            // One atomic JS step: re-translate content to the new ActualOffset, grow the host so the
            // scroll range covers the (possibly auto-extended) model content, then scroll — all in a
            // single synchronous block the browser paints as one frame, so there is no intermediate
            // frame where the world sits at the old translate under the new scroll (the old left-right
            // flicker). Effective-space lengths: the JS adds its own edge reserve back.
            //
            // Node geometry joins the same atomic block: after Scale changed, every node's collapsed
            // Anchor/Size getter (world / scale) is already correct, so we marshal them and the JS
            // repositions the existing pooled wrappers synchronously here, then keeps re-asserting them
            // (surfaceZoomState settle loop) until the async .NET per-node renders converge — so a
            // stale render can never paint even one frame of old collapsed values.
            // Await the single atomic apply so the DOM (translate + host grow + scroll + node/link
            // geometry) is fully stamped before this burst returns — the next burst's live DOM read is
            // then the settled final state, never a half-applied intermediate.
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
            // Wheel up (positive wheelDelta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
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

        // Keep the .NET-side canvas size and content translate in sync with JS-side auto-expansion
        // (grow-only), so subsequent re-renders never shrink the canvas back after it was expanded
        // near an edge, and never reset the left/top offset while the user is panning.
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
            // Canonical (reported) frame — matches the XAML adapters: the reserved ruler inset and any
            // left/top overscroll growth are a visual translate only, so the world-left reported here is
            // scroll − (ActualOffset + overscroll). The decorator re-adds the ruler reserve for ticks.
            var effX = contentX + Math.Max(0, _offsetX - RulerThickness);
            var effY = contentY + Math.Max(0, _offsetY - RulerThickness);
            var viewportX = WorkflowSurfaceMath.ToWorld(scrollLeft, effX);
            var viewportY = WorkflowSurfaceMath.ToWorld(scrollTop, effY);

            // Keep the virtualization visible-region correction in sync with the reserved ruler band
            // so nodes beneath it are not culled a ruler-thickness early.
            Tree.SetVirtualizeInset(left: RulerThickness, top: RulerThickness);
            try
            {
                Tree.GetHelper().Viewport = new Viewport(viewportX, viewportY, _viewportW, _viewportH);
            }
            catch
            {
                // Best-effort viewport bookkeeping; some trees may not support it.
            }

            // Persist the viewport position (world coordinates) so it survives a serialization
            // round-trip — mirrors the XAML adapters' WorkflowSurfaceBehavior.UpdateVisibleRegion.
            if (layout is not null)
            {
                layout.ViewportOffset = new Offset(viewportX, viewportY);
            }

            _viewport = BuildViewport();

            // Cheap overlays (grid decorator) update via the feed; the node/link content subtree is
            // untouched by plain scrolling. The canvas host/grid/axis are JS-owned, so the grid no
            // longer depends on a re-render. Only canvas growth (edge expansion) changes the links-
            // layer size (SurfaceCanvas context), so only then do we re-render the whole surface.
            _feed.Publish(_viewport);
            // The minimap is drawn over node/world content, so it must use the PHYSICAL visible world
            // rect (which includes the edge translate) — not the canonical (ruler-excluded) one.
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
