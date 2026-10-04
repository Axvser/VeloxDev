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
    public string Background { get; set; } = "#D2141922";

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
    public IWorkflowTreeViewModel? WorkflowTree { get; set; }

    /// <inheritdoc />
    public bool IsMinimapVisible { get; set; } = true;

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

    // 事件驱动的内容跟踪：只在节点增/删/移动时（去抖地）重算映射/节点矩形，与 XAML 几家的 MarkDirty + 16ms 定时器一致。
    private IWorkflowTreeViewModel? _subscribedTree;
    private INotifyCollectionChanged? _nodesNotifier;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly object _rebuildLock = new();
    private bool _rebuildScheduled;
    private CancellationTokenSource? _throttleCts;

    private IReadOnlyList<Mapped>? MappedNodes { get; set; }

    // CSS style 属性内无单位长度无效（浏览器会丢弃，元素塌成 0×0）。这些值喂给内联 width/height 样式，下面 SVG 的 width/height 属性也接受 px 后缀。
    // 固定不变并用 '.' 分隔：它们喂给 style 与 SVG 长度属性，逗号小数文化会序列化出浏览器解析不了的值。
    private string WidthCss => Width.ToString("0.#", CultureInfo.InvariantCulture) + "px";
    private string HeightCss => Height.ToString("0.#", CultureInfo.InvariantCulture) + "px";
    private string NodeRadiusCss => NodeRadius.ToString("0.#", CultureInfo.InvariantCulture);
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
            || Math.Abs(Height - _lastHeight) > double.Epsilon)
        {
            _lastWidth = Width;
            _lastHeight = Height;
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

    // ── 内容跟踪（事件驱动，与 XAML 几家一致）─────────────────────────────

    private void ResubscribeTree(IWorkflowTreeViewModel? tree)
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
                if (item is IWorkflowNodeViewModel node && node is INotifyPropertyChanged npc)
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
                if (item is IWorkflowNodeViewModel node
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
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
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

    private void Recompute()
    {
        var tree = WorkflowTree;
        if (tree is null)
        {
            MappedNodes = null;
            return;
        }

        var nodes = tree.Nodes?.ToArray() ?? Array.Empty<IWorkflowNodeViewModel>();
        if (nodes.Length == 0)
        {
            MappedNodes = Array.Empty<Mapped>();
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
        var drawW = Math.Max(1, Width - pad * 2);
        var drawH = Math.Max(1, Height - pad * 2);
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
