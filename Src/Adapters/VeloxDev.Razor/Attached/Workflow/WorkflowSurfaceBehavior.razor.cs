using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;

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

    // Broadcasts the latest viewport snapshot to cheap overlay consumers (grid decorator) so they
    // can re-render without dragging the node/link content subtree along. See SurfaceViewportFeed.
    private readonly SurfaceViewportFeed _feed = new();

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
            return $"background-color:{Background};" +
                   $"--veloxdev-gs:{spacing.ToString("0.#")}px;" +
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
            _handle = await _module.InvokeAsync<IJSObjectReference>("initSurface",
                _scroller, _canvasHost, _dotNetRef, _canvasW, _canvasH, contentX, contentY, _offsetX, _offsetY);

            if (ZoomEnabled)
            {
                _wheelHandle = await _module.InvokeAsync<IJSObjectReference>("initWheelZoom", _scroller, _dotNetRef);
            }
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
    public void OnWheelZoom(int wheelDelta)
    {
        if (Tree is null)
        {
            return;
        }

        var layout = Tree.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
        {
            // Capture the world point under the viewport center ONCE for the whole burst — the scroll
            // state is unchanged until the single final apply, so the same pivot stays valid across
            // every compounding step.
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                _scrollLeft - _offsetX, _scrollTop - _offsetY, _viewportW, _viewportH, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);

            var notches = Math.Abs(wheelDelta) / 120d;
            var count = (int)Math.Max(1, Math.Round(notches));
            var factor = wheelDelta > 0 ? 1.1 : 1 / 1.1;

            for (var i = 0; i < count; i++)
            {
                var next = Math.Max(0.1, Math.Min(10, layout.Scale.Horizontal * factor));
                layout.Scale = new Scale(next, next);
            }

            // The canvas content auto-extends on zoom-in below scale 1 (ActualSize = world / scale),
            // and clamping may grow NegativeOffset (content moves right/down). Push the new offset and
            // the new extent to the DOM atomically with the scroll below; first compute the scroll
            // against the post-change model extent exactly like the XAML adapters.
            //
            // Content width = the model ActualSize (the links layer) but at least the DOM host's
            // currently-reachable width (_canvasW − edge) so an edge-pan-expanded host is never clamped
            // shorter than what the user already scrolled to. The clamp max is the effective scroll
            // extent (content − viewport), matching what the JS host will expose after we grow it.
            var contentW = Math.Max(1, Math.Max(layout.ActualSize.Width, _canvasW - _offsetX));
            var contentH = Math.Max(1, Math.Max(layout.ActualSize.Height, _canvasH - _offsetY));
            var (tx0, ty0) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, _viewportW, _viewportH);
            _ = WorkflowSurfaceMath.ClampScrollOffset(tx0, Math.Max(0, contentW - _viewportW), layout, horizontal: true);
            _ = WorkflowSurfaceMath.ClampScrollOffset(ty0, Math.Max(0, contentH - _viewportH), layout, horizontal: false);

            // The clamp may have grown NegativeOffset, which moved the content — re-derive the scroll
            // from the NEW offset/scale/extent so the pivot lands exactly under the viewport center
            // (first-pass PivotCenterScroll used the pre-clamp offset).
            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, _viewportW, _viewportH);
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
            if (_module is not null && !string.IsNullOrWhiteSpace(ScrollViewerId))
            {
                _ = _module.InvokeVoidAsync("applyZoomSurface",
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
            var factor = wheelDelta > 0 ? 1.1 : 1 / 1.1;
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
            var viewportX = WorkflowSurfaceMath.ToWorld(scrollLeft, _offsetX + contentX);
            var viewportY = WorkflowSurfaceMath.ToWorld(scrollTop, _offsetY + contentY);
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
            PushMinimapViewport(viewportX, viewportY);
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
            _scrollLeft - _offsetX,
            _scrollTop - _offsetY,
            _viewportW,
            _viewportH,
            Tree.Layout?.ActualOffset.Horizontal ?? 0,
            Tree.Layout?.ActualOffset.Vertical ?? 0);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
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
