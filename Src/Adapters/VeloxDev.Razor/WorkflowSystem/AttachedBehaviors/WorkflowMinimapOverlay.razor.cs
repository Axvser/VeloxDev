using System;
using System.Collections.Generic;
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
/// Tree subscription, content/viewport mapping, placement and the render context live in the Core
/// <see cref="MinimapPipelineCore{TSurface,TRenderer,TViewport,TInput}"/>; this component supplies a
/// marker renderer that hands the context to markup and the existing <see cref="DrawMinimap"/>
/// fragment. The viewport-block indicator is moved directly by JavaScript as the surface scrolls, so
/// scrolling costs one tiny interop instead of a full .NET re-render of every node rect. Drag
/// navigation also stays in JavaScript: the Core drag math writes overscroll into
/// <see cref="CanvasLayout.NegativeOffset"/> / <see cref="CanvasLayout.PositiveOffset"/>, while this
/// adapter keeps overscroll in JS-only edge offsets outside the model, so applying it here would
/// translate the content twice.
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

    // Core 状态下这层只保留渲染期快照与 JS 握手：订阅、缓存、映射、放置都在 MinimapPipelineCore。
    private readonly RazorMinimapViewportHost _viewportHost;
    private readonly RazorMinimapInputHost _inputHost;
    private readonly MarkerMinimapRenderer _renderer;
    private readonly MinimapPipelineCore<WorkflowMinimapOverlay, MarkerMinimapRenderer, RazorMinimapViewportHost, RazorMinimapInputHost> _pipeline;

    private MinimapDrawContext? _drawContext;
    private IReadOnlyList<WorkflowBounds>? _mappedNodes;

    private double _rectLeft;
    private double _rectTop;
    private double _rectWidth = 180;
    private double _rectHeight = 120;
    private double _rectRight;
    private double _overlayWidth;
    private bool _areaMeasured;

    // JS 回报的滚动上限；Core 的 viewport 契约是同步读，而 DOM 只能异步问，所以这里缓存最近一次读数。
    private double _scrollableWidth;
    private double _scrollableHeight;

    private double _lastWidth = 180;
    private double _lastHeight = 120;
    private double _lastMargin = 12;

    private const double MinimapMinSize = 20;

    // 树内容变化由 Core 的 RenderRequested 通知；这里只做 16ms 去抖 + 编组到渲染器上下文。
    private readonly object _rebuildLock = new();
    private bool _rebuildScheduled;
    private CancellationTokenSource? _throttleCts;
    private bool _suppressRenderRequested;
    private bool _disposed;

    /// <summary>Creates the overlay and its Core pipeline.</summary>
    public WorkflowMinimapOverlay()
    {
        _viewportHost = new RazorMinimapViewportHost(this);
        _inputHost = new RazorMinimapInputHost();
        _renderer = new MarkerMinimapRenderer(this);
        _pipeline = new MinimapPipelineCore<WorkflowMinimapOverlay, MarkerMinimapRenderer, RazorMinimapViewportHost, RazorMinimapInputHost>(
            _renderer, _viewportHost, _inputHost);
        _pipeline.RenderRequested += OnPipelineRenderRequested;
    }

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

    // 渲染片段拿到的上下文；为空时用放置尺寸拼一个空上下文，与旧实现的 EmptyContext 同形。
    private MinimapDrawContext DrawContext => _drawContext ?? EmptyContext();

    private IReadOnlyList<WorkflowBounds>? MappedNodes => _mappedNodes;

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

            if (!ReferenceEquals(_pipeline.Tree, vp.Tree))
            {
                _suppressRenderRequested = true;
                try
                {
                    _pipeline.Tree = vp.Tree;
                }
                finally
                {
                    _suppressRenderRequested = false;
                }

                WorkflowTree = vp.Tree;
                recompute = true;
            }
            else
            {
                WorkflowTree = vp.Tree;
            }
        }
        else if (_pipeline.Tree is not null)
        {
            _suppressRenderRequested = true;
            try
            {
                _pipeline.Tree = null;
            }
            finally
            {
                _suppressRenderRequested = false;
            }

            WorkflowTree = null;
            recompute = true;
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

        ApplyMetrics();

        // 内容适配映射只取决于节点边界 + 缩略图尺寸，绝不取决于滚动；滚动由 JS 移动视口块处理。
        if (recompute || _pipeline.IsDirty)
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
        if (_disposed)
        {
            return;
        }

        AvailableRight = Math.Max(0, availableRight);
        AvailableTop = Math.Max(0, availableTop);
        AvailableBottom = Math.Max(0, availableBottom);
        _overlayWidth = Math.Max(0, overlayWidth);
        _areaMeasured = true;

        // 可用区变了，矩形可能被夹小 —— 放置与映射都按新矩形重算，再让标记按新位置渲染。
        ApplyMetrics();
        Recompute();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Receives the surface's current scroll range from JavaScript. The Core
    /// <see cref="IMinimapViewportHost"/> reads it synchronously; the DOM can only be asked
    /// asynchronously, so the last reported value is kept here.
    /// </summary>
    /// <param name="maximumX">The maximum horizontal scroll offset.</param>
    /// <param name="maximumY">The maximum vertical scroll offset.</param>
    [JSInvokable]
    public void OnMinimapScrollRange(double maximumX, double maximumY)
    {
        _scrollableWidth = Math.Max(0, maximumX);
        _scrollableHeight = Math.Max(0, maximumY);
    }

    // ── 管线同步 ────────────────────────────────────────────────────────────

    private void ApplyMetrics()
    {
        _pipeline.Metrics = new MinimapMetrics
        {
            Width = Math.Max(0, Width),
            Height = Math.Max(0, Height),
            MinSize = MinimapMinSize,
            Padding = Math.Max(0, Padding),
            NodeCornerRadius = Math.Max(0, NodeRadius),
            // 旧实现的 DrawContext 未设置框圆角（CSS 边框负责外观），保持 0 以免改变自定义片段的可见结果。
            MinimapCornerRadius = 0d,
            // Razor 旧实现缩略图下限是 1px（Jalium 同），与其余几家的 2px 不同，保持本家现状。
            MinimumThumbSize = 1d,
            Available = new SurfaceAvailableArea
            {
                Right = AvailableRight,
                Top = AvailableTop,
                Bottom = AvailableBottom,
                Margin = MinimapMargin,
            },
        };
        _pipeline.IsVisible = IsMinimapVisible;
    }

    // 让 Core 重算映射并把上下文交给标记渲染器；无论有无内容都刷新放置矩形。
    private void Recompute()
    {
        _pipeline.Render(this);
        _drawContext = _pipeline.DrawContext;
        _mappedNodes = _drawContext?.MappedNodes;
        UpdatePlacement();
        PushMapping();
    }

    // 可用区 → 小地图矩形。首次测量前 Available* 为 0，Core 退回原点 + 请求尺寸；内层盒子此时隐藏。
    private void UpdatePlacement()
    {
        var bounds = _pipeline.MinimapRect;
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

    /// <summary>
    /// Pushes the content-fit mapping used to render the minimap to JS, so the JS-driven viewport
    /// block inverts the SAME scale instead of a raw scroll-extent ratio (which diverges once the
    /// canvas is edge-extended and overshoots by n×). Called on every content rebuild.
    /// </summary>
    private void PushMapping()
    {
        if (_module is null || string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            return;
        }

        var context = _pipeline.DrawContext;
        if (context is null || context.Scale <= 0d)
        {
            _ = _module.InvokeVoidAsync("setMinimapMapping", ScrollViewerId, 0d, 0d, 0d, 0d, 0d, 0d, 0d);
            return;
        }

        var content = context.ContentBounds;
        _ = _module.InvokeVoidAsync("setMinimapMapping",
            ScrollViewerId,
            context.Scale, context.OriginX, context.OriginY,
            content.Left, content.Top, content.Right, content.Bottom);
    }

    // Core 的 RenderRequested 可能从任意线程来；去抖后编组回渲染器上下文。
    private void OnPipelineRenderRequested(object? sender, EventArgs e)
    {
        if (_suppressRenderRequested || _disposed)
        {
            return;
        }

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

    // 标记渲染器：Blazor 没有可绘制的表面，真正绘制由标记读取这份上下文完成。
    private void OnMinimapRendered(MinimapDrawContext context) => _drawContext = context;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _pipeline.RenderRequested -= OnPipelineRenderRequested;
        _pipeline.Dispose();

        _throttleCts?.Cancel();
        _throttleCts?.Dispose();
        lock (_rebuildLock)
        {
            _rebuildScheduled = false;
        }

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

    /// <summary>Hands every pipeline pass to the component's markup instead of a drawing surface.</summary>
    private sealed class MarkerMinimapRenderer : IMinimapRenderer<WorkflowMinimapOverlay>
    {
        private readonly WorkflowMinimapOverlay owner;

        public MarkerMinimapRenderer(WorkflowMinimapOverlay owner) => this.owner = owner;

        /// <inheritdoc />
        public void Render(WorkflowMinimapOverlay surface, MinimapDrawContext context)
            => owner.OnMinimapRendered(context);
    }

    // Core 的滚动读写口。读值来自表面参数；滚动上限来自 JS 最近一次回报。
    private sealed class RazorMinimapViewportHost : IMinimapViewportHost
    {
        private readonly WorkflowMinimapOverlay owner;

        public RazorMinimapViewportHost(WorkflowMinimapOverlay owner) => this.owner = owner;

        public double ScrollOffsetX => owner.ScrollOffsetX;

        public double ScrollOffsetY => owner.ScrollOffsetY;

        public double ContentOffsetX => owner.ContentOffsetX;

        public double ContentOffsetY => owner.ContentOffsetY;

        public double ViewportWidth => owner.ViewportWidth;

        public double ViewportHeight => owner.ViewportHeight;

        public double ScrollableWidth => owner._scrollableWidth;

        public double ScrollableHeight => owner._scrollableHeight;

        public void ApplyScroll(double x, double y)
        {
            // 管线拖拽路径当前不启用（见类注释的越界模型差异）；保留实现以便 Core 侧启用时可直接接上。
            if (owner._module is null || string.IsNullOrWhiteSpace(owner.ScrollViewerId))
            {
                return;
            }

            _ = owner._module.InvokeVoidAsync("scrollToPosition", owner.ScrollViewerId, x, y);
        }
    }

    // Core 需要指针层才能构造；拖拽导航仍在 JS（见类注释），所以这里只保留契约形状。
    private sealed class RazorMinimapInputHost : IMinimapInputHost
    {
        public (double X, double Y) PointerPosition { get; set; }

        public bool IsPressed { get; set; }

        public bool IsCaptured { get; private set; }

        public WorkflowBounds ClampRect { get; set; }

        public bool Capture()
        {
            // JS 在 document 上跟踪拖拽，不需要额外捕获。
            IsCaptured = true;
            return true;
        }

        public void Release() => IsCaptured = false;
    }
}
