using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Minimap overlay that renders a thumbnail overview of all nodes plus the visible viewport
/// rectangle. Consumes a <see cref="SurfaceViewport"/> context and implements
/// <see cref="IWorkflowMinimapOverlay"/> for API parity with the XAML adapters.
///
/// The component renders a full-surface, click-through layer; the inner minimap box is placed at
/// the content viewport's top-right corner (below the ruler band, left of the vertical scrollbar,
/// above the horizontal one) using the available area measured by JavaScript.
///
/// Mirrors the XAML adapters' architecture: the content-fit mapping and node rects are recomputed
/// only when the tree content changes (nodes added/removed/moved — debounced 16ms), never on plain
/// scroll. The viewport-block indicator is moved directly by JavaScript as the surface scrolls, so
/// scrolling costs one tiny interop instead of a full .NET re-render of every node rect.
///
/// Navigation is grab-the-block (matching the other adapters): pressing must land on the viewport
/// block; the grab point is centered on the surface viewport, and dragging pans with edge expansion.
/// </summary>
public partial class WorkflowMinimapOverlay : ComponentBase, IWorkflowMinimapOverlay, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>Gets or sets the surface viewport context pushed by <see cref="WorkflowSurfaceBehavior"/>.</summary>
    [Parameter]
    public SurfaceViewport? Viewport { get; set; }

    /// <summary>Gets or sets the minimap width in pixels.</summary>
    [Parameter]
    public double Width { get; set; } = 180;

    /// <summary>Gets or sets the minimap height in pixels.</summary>
    [Parameter]
    public double Height { get; set; } = 120;

    /// <summary>Gets or sets the minimap background color.</summary>
    [Parameter]
    public string Background { get; set; } = "#331E1E1E";

    /// <summary>Gets or sets the minimap border color.</summary>
    [Parameter]
    public string BorderColor { get; set; } = "#DC94A3B8";

    /// <summary>Gets or sets the node fill color.</summary>
    [Parameter]
    public string NodeFill { get; set; } = "#DC38BDF8";

    /// <summary>Gets or sets the node corner radius in pixels.</summary>
    [Parameter]
    public double NodeRadius { get; set; } = 2;

    /// <summary>
    /// Gets or sets the viewport indicator fill color. Defaults to a subtle white fill
    /// (<c>#28FFFFFF</c>), matching the WPF/Avalonia/MAUI/WinUI adapters. Item templates and
    /// trimmed demos override this to <c>transparent</c> so their indicator renders as an
    /// outline only.
    /// </summary>
    [Parameter]
    public string ViewportFill { get; set; } = "rgba(255,255,255,0.15)";

    /// <summary>Gets or sets the viewport indicator stroke color.</summary>
    [Parameter]
    public string ViewportStroke { get; set; } = "#F0FFFFFF";

    /// <summary>Gets or sets the viewport indicator stroke width.</summary>
    [Parameter]
    public double ViewportStrokeWidth { get; set; } = 1.5;

    /// <summary>Gets or sets the inner padding around the mapped content in pixels.</summary>
    [Parameter]
    public double Padding { get; set; } = 6;

    /// <summary>Gets or sets the id of the surface scroll container that navigation should scroll.</summary>
    [Parameter]
    public string? ScrollViewerId { get; set; }

    /// <summary>
    /// Gets or sets extra minimap content rendered after the default nodes and viewport block.
    /// </summary>
    /// <remarks>
    /// Blazor components cannot override a base component's render tree, so this render fragment is the
    /// equivalent of the <c>DrawMinimap</c> override the class-based adapters expose.
    /// </remarks>
    [Parameter]
    public RenderFragment<MinimapDrawContext>? DrawMinimap { get; set; }

    /// <inheritdoc />
    public double ScrollOffsetX { get; set; }

    /// <inheritdoc />
    public double ScrollOffsetY { get; set; }

    /// <inheritdoc />
    public double ContentOffsetX { get; set; }

    /// <inheritdoc />
    public double ContentOffsetY { get; set; }

    /// <inheritdoc />
    public double RulerBand => 0;

    /// <inheritdoc />
    public double ViewportWidth { get; set; }

    /// <inheritdoc />
    public double ViewportHeight { get; set; }

    /// <inheritdoc />
    public ITreeComponent? WorkflowTree { get; set; }

    /// <inheritdoc />
    public bool IsMinimapVisible { get; set; } = true;

    /// <summary>Gets or sets the gap in pixels kept between the minimap and the available corner.</summary>
    [Parameter]
    public double MinimapMargin { get; set; } = 12;

    /// <inheritdoc />
    public double AvailableRight { get; set; }

    /// <inheritdoc />
    public double AvailableTop { get; set; }

    /// <inheritdoc />
    public double AvailableBottom { get; set; }

    private ElementReference _element;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WorkflowMinimapOverlay>? _dotNetRef;
    private IJSObjectReference? _handle;

    // 渲染缩略图用的内容适配映射。它推给 JS，拖拽/点击导航才能反演同一份映射（而不是原始滚动范围比例 —— 画布越界扩展后它就会偏离、并过冲 n 倍）。只有下面这些字段随内容边界变化。
    private double _scale;
    private double _mapOx;
    private double _mapOy;
    private double _minX;
    private double _minY;
    private double _maxX;
    private double _maxY;

    private double _lastWidth;
    private double _lastHeight;
    private double _lastMargin;

    // 小地图矩形：由可用区（Available*）算出；绘制、内容映射与内层盒子的定位共用这一份。
    private double _rectLeft;
    private double _rectTop;
    private double _rectWidth = 180;
    private double _rectHeight = 120;
    private double _rectRight;
    private double _overlayWidth;
    private bool _areaMeasured;

    private const double MinimapMinSize = 20;

    // 事件驱动的内容跟踪：只在节点增/删/移动时（去抖地）重算映射/节点矩形，与 XAML 几家的 MarkDirty + 16ms 定时器一致。
    private ITreeComponent? _subscribedTree;
    private INotifyCollectionChanged? _nodesNotifier;
    private readonly HashSet<INodeComponent> _subscribedNodes = [];
    private readonly object _rebuildLock = new();
    private bool _rebuildScheduled;
    private CancellationTokenSource? _throttleCts;

    private IReadOnlyList<Mapped>? MappedNodes { get; set; }

    private MinimapDrawContext DrawContext { get; set; } = new();

    // CSS style 属性内无单位长度无效（浏览器会丢弃，元素塌成 0×0）。这些值喂给内联 width/height 样式，下面 SVG 的 width/height 属性也接受 px 后缀。
    // 固定不变并用 '.' 分隔：它们喂给 style 与 SVG 长度属性，逗号小数文化会序列化出浏览器解析不了的值。
    private string RectWidthCss => Css(_rectWidth) + "px";
    private string RectHeightCss => Css(_rectHeight) + "px";

    // 首次测量回来之前内层盒子藏起来：可用区还是 0 时矩形落在原点，先画出来会闪一下再跳到右上角。
    // 没有滚动容器 id 时无法测量，沿用旧的贴角落位置。
    private string RectTopCss => (_areaMeasured ? Css(_rectTop) : "8") + "px";
    private string RectRightCss => (_areaMeasured ? Css(_rectRight) : "8") + "px";
    private string InnerVisibilityCss =>
        _areaMeasured || string.IsNullOrWhiteSpace(ScrollViewerId) ? "visible" : "hidden";

    private static string Css(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    private string NodeRadiusCss => Css(NodeRadius);
    private string ViewportStrokeWidthCss => ViewportStrokeWidth.ToString("0.#", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        var recompute = false;
        if (Viewport is { } vp)
        {
            ScrollOffsetX = vp.ScrollLeft;
            ScrollOffsetY = vp.ScrollTop;
            ContentOffsetX = vp.ContentOffsetX;
            ContentOffsetY = vp.ContentOffsetY;
            ViewportWidth = vp.ViewportWidth;
            ViewportHeight = vp.ViewportHeight;

            if (!ReferenceEquals(_subscribedTree, vp.Tree))
            {
                WorkflowTree = vp.Tree;
                ResubscribeTree(vp.Tree);
                recompute = true;
            }
        }

        if (Math.Abs(Width - _lastWidth) > double.Epsilon
            || Math.Abs(Height - _lastHeight) > double.Epsilon
            || Math.Abs(MinimapMargin - _lastMargin) > double.Epsilon)
        {
            _lastWidth = Width;
            _lastHeight = Height;
            _lastMargin = MinimapMargin;
            recompute = true;
        }

        // 内容适配映射只取决于节点边界 + 缩略图尺寸，绝不取决于滚动；滚动由 JS 移动视口块处理，所以视口变化时不重算。
        if (recompute)
        {
            Recompute();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender && !string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/VeloxDev.Razor/veloxdev.workflow.js");
            _dotNetRef = DotNetObjectReference.Create(this);
            _handle = await _module.InvokeAsync<IJSObjectReference>("initMinimap", _element, ScrollViewerId, _dotNetRef);
            PushMapping();
        }

        // 每次重渲染后重新同步 JS 持有的视口块：重渲染会用陈旧的 .NET 状态（MappedViewport）重写它，而 refreshMinimapViewport 会再应用表面最近推过的当前世界矩形，块在平移中因此不会跳走。
        if (_module is not null && !string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            _ = _module.InvokeVoidAsync("refreshMinimapViewport", ScrollViewerId);
        }
    }

    /// <summary>
    /// Receives the free corner measured by JavaScript: <paramref name="availableRight"/> excludes the
    /// vertical scrollbar, <paramref name="availableTop"/> is the horizontal ruler band height, and
    /// <paramref name="availableBottom"/> excludes the horizontal scrollbar;
    /// <paramref name="overlayWidth"/> is the full-surface layer's width, used to express the inner
    /// box's <c>right</c> offset.
    /// </summary>
    [JSInvokable]
    public void OnMinimapAreaMeasured(double availableRight, double availableTop, double availableBottom, double overlayWidth)
    {
        AvailableRight = Math.Max(0, availableRight);
        AvailableTop = Math.Max(0, availableTop);
        AvailableBottom = Math.Max(0, availableBottom);
        _overlayWidth = Math.Max(0, overlayWidth);
        _areaMeasured = true;

        // 可用区变了，矩形可能被夹小 —— 映射和视口块都按矩形尺寸重算，再让标记按新位置渲染。
        Recompute();
        _ = InvokeAsync(StateHasChanged);
    }

    // ── 内容跟踪（事件驱动，与 XAML 几家一致）─────────────────────────────

    private void ResubscribeTree(ITreeComponent? tree)
    {
        if (_nodesNotifier is not null)
        {
            _nodesNotifier.CollectionChanged -= OnNodesChanged;
            _nodesNotifier = null;
        }

        foreach (var node in _subscribedNodes)
        {
            if (node is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        _subscribedNodes.Clear();
        _subscribedTree = tree;

        if (tree?.Nodes is { } nodes)
        {
            if (nodes is INotifyCollectionChanged nc)
            {
                _nodesNotifier = nc;
                nc.CollectionChanged += OnNodesChanged;
            }

            foreach (var node in nodes)
            {
                if (node is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged += OnNodePropertyChanged;
                    _subscribedNodes.Add(node);
                }
            }
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INodeComponent node && node is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged += OnNodePropertyChanged;
                    _subscribedNodes.Add(node);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INodeComponent node
                    && _subscribedNodes.Remove(node)
                    && node is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged -= OnNodePropertyChanged;
                }
            }
        }

        MarkDirty();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size))
        {
            MarkDirty();
        }
    }

    /// <summary>
    /// Throttles rapid content changes (e.g. a node being dragged fires Anchor changes every frame)
    /// to at most one rebuild per 16ms — the same fixed-interval timer the XAML adapters' minimaps
    /// use, so the node rects keep tracking a live drag instead of waiting for it to stop.
    /// </summary>
    private void MarkDirty()
    {
        lock (_rebuildLock)
        {
            if (_rebuildScheduled)
            {
                return;
            }

            _rebuildScheduled = true;
        }

        _throttleCts?.Cancel();
        _throttleCts?.Dispose();
        var cts = new CancellationTokenSource();
        _throttleCts = cts;
        _ = RebuildAfterThrottleAsync(cts.Token);
    }

    private async Task RebuildAfterThrottleAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(16, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_rebuildLock)
        {
            _rebuildScheduled = false;
        }

        await InvokeAsync(() =>
        {
            Recompute();
            StateHasChanged();
        });
    }

    // ── 映射 ────────────────────────────────────────────────────────────────

    // 可用区 → 小地图矩形。首次测量前 Available* 为 0，Core 退回原点 + 请求尺寸；内层盒子此时隐藏。
    private MinimapDrawContext CreatePlacementContext()
        => new()
        {
            MinimapWidth = Width,
            MinimapHeight = Height,
            MinimapMargin = MinimapMargin,
            AvailableRight = AvailableRight,
            AvailableTop = AvailableTop,
            AvailableBottom = AvailableBottom,
        };

    private void UpdatePlacement()
    {
        var bounds = CreatePlacementContext().GetMinimapRect(MinimapMinSize);
        _rectLeft = bounds.Left;
        _rectTop = bounds.Top;
        _rectWidth = Math.Max(1, bounds.Width);
        _rectHeight = Math.Max(1, bounds.Height);
        // 内层盒子用 top/right 定位；right 相对整块浮层（宽度 = 表面宽）来说。
        _rectRight = Math.Max(0, _overlayWidth - (_rectLeft + _rectWidth));
    }

    private MinimapDrawContext EmptyContext()
        => new()
        {
            MinimapWidth = _rectWidth,
            MinimapHeight = _rectHeight,
            Padding = Math.Max(0, Padding),
            AvailableRight = AvailableRight,
            AvailableTop = AvailableTop,
            AvailableBottom = AvailableBottom,
            MinimapMargin = MinimapMargin,
        };

    private void Recompute()
    {
        UpdatePlacement();

        var tree = WorkflowTree;
        if (tree is null)
        {
            MappedNodes = null;
            DrawContext = EmptyContext();
            return;
        }

        var nodes = tree.Nodes?.ToArray() ?? Array.Empty<INodeComponent>();
        if (nodes.Length == 0)
        {
            MappedNodes = Array.Empty<Mapped>();
            DrawContext = EmptyContext();
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in nodes)
        {
            minX = Math.Min(minX, n.Anchor.Horizontal);
            minY = Math.Min(minY, n.Anchor.Vertical);
            maxX = Math.Max(maxX, n.Anchor.Horizontal + n.Size.Width);
            maxY = Math.Max(maxY, n.Anchor.Vertical + n.Size.Height);
        }

        var pad = Math.Max(0, Padding);
        // 映射按矩形尺寸算（可用区可能把它夹得比请求的 Width/Height 小），绘制与拖拽反演才能对齐。
        var drawW = Math.Max(1, _rectWidth - pad * 2);
        var drawH = Math.Max(1, _rectHeight - pad * 2);
        var bw = Math.Max(1, maxX - minX);
        var bh = Math.Max(1, maxY - minY);
        var (ox, oy, scale) = WorkflowSurfaceMath.MinimapFit(bw, bh, drawW, drawH, pad);

        _scale = scale;
        _mapOx = ox;
        _mapOy = oy;
        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
        PushMapping();

        MappedNodes = nodes.Select(n =>
        {
            var (x, y) = WorkflowSurfaceMath.MinimapLocal(n.Anchor.Horizontal, n.Anchor.Vertical, minX, minY, ox, oy, scale);
            return new Mapped(x, y, Math.Max(1, n.Size.Width * scale), Math.Max(1, n.Size.Height * scale));
        }).ToArray();

        var contentBounds = WorkflowBounds.FromNode(minX, minY, bw, bh);
        var viewportBounds = WorkflowBounds.FromNode(
            WorkflowSurfaceMath.ToCanvasLocal(ScrollOffsetX, ContentOffsetX),
            WorkflowSurfaceMath.ToCanvasLocal(ScrollOffsetY, ContentOffsetY),
            ViewportWidth, ViewportHeight);
        var mappedViewport = default(WorkflowBounds);
        if (!viewportBounds.IsEmpty)
        {
            var (vx, vy, vw, vh) = WorkflowSurfaceMath.MinimapViewportRect(
                ox, oy, scale, viewportBounds.Left, viewportBounds.Top, viewportBounds.Width, viewportBounds.Height,
                minX, minY, _rectWidth, _rectHeight, minRectSize: 2.0);
            mappedViewport = WorkflowBounds.FromNode(vx, vy, vw, vh);
        }

        DrawContext = new MinimapDrawContext
        {
            Tree = tree,
            ContentBounds = contentBounds,
            ViewportBounds = viewportBounds,
            Nodes = nodes.Select(n => WorkflowBounds.FromNode(
                n.Anchor.Horizontal, n.Anchor.Vertical, n.Size.Width, n.Size.Height)).ToArray(),
            MappedNodes = MappedNodes.Select(n => WorkflowBounds.FromNode(n.X, n.Y, n.W, n.H)).ToArray(),
            MappedViewport = mappedViewport,
            MinimapWidth = _rectWidth,
            MinimapHeight = _rectHeight,
            Padding = pad,
            OriginX = ox,
            OriginY = oy,
            Scale = scale,
            NodeCornerRadius = NodeRadius,
            AvailableRight = AvailableRight,
            AvailableTop = AvailableTop,
            AvailableBottom = AvailableBottom,
            MinimapMargin = MinimapMargin,
        };

        // 视口块不在这里计算/渲染：它完全归 JS（setMinimapViewport），.NET 重建因此永远不会用陈旧坐标覆盖它。
    }

    /// <summary>
    /// Pushes the content-fit mapping used to render the minimap to JS, so drag/click
    /// navigation and the JS-driven viewport block invert the SAME scale instead of a raw
    /// scroll-extent ratio (which diverges once the canvas is edge-extended and overshoots by n×).
    /// Called on every content rebuild.
    /// </summary>
    private void PushMapping()
    {
        if (_module is not null && !string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            _ = _module.InvokeVoidAsync("setMinimapMapping", ScrollViewerId, _scale, _mapOx, _mapOy, _minX, _minY, _maxX, _maxY);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _throttleCts?.Cancel();
        _throttleCts?.Dispose();
        lock (_rebuildLock)
        {
            _rebuildScheduled = false;
        }

        ResubscribeTree(null);

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

    private sealed record Mapped(double X, double Y, double W, double H)
    {
        public string XCss => X.ToString("0.#", CultureInfo.InvariantCulture);
        public string YCss => Y.ToString("0.#", CultureInfo.InvariantCulture);
        public string WCss => W.ToString("0.#", CultureInfo.InvariantCulture);
        public string HCss => H.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
