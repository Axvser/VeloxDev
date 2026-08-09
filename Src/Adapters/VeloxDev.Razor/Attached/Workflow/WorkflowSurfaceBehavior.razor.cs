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
    public bool IsEnabled { get; set; } = true;

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
    private ElementReference _canvas;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WorkflowSurfaceBehavior>? _dotNetRef;
    private IJSObjectReference? _handle;

    private double _scrollLeft;
    private double _scrollTop;
    private double _viewportW = 800;
    private double _viewportH = 600;
    private double _canvasW = 1600;
    private double _canvasH = 1200;

    // Content translate for left/top canvas expansion. The canvas element grows in all four
    // directions; the content wrapper is shifted right/down by this offset so world (node/slot)
    // coordinates stay put while the newly revealed area appears to the left/top.
    private double _offsetX;
    private double _offsetY;
    private SurfaceViewport _viewport = null!;

    private double ContentWidth => Math.Max(1, _canvasW - _offsetX);
    private double ContentHeight => Math.Max(1, _canvasH - _offsetY);

    private string CanvasStyle
    {
        get
        {
            var spacing = Math.Max(8, GridSpacing);
            var majorStep = spacing * Math.Max(1, MajorLineEvery);

            // The canvas is painted in its own coordinates: world v sits at canvas x = _offsetX + v
            // (the content wrapper is translated by _offsetX/_offsetY, so world 0 lands on the
            // ruler/content boundary). Each grid layer is a repeating-linear-gradient whose tile
            // spans [0, period] and whose 1px line sits at `phase` = offset % period, so lines land
            // on world multiples of spacing (minor) / majorStep (major). The FIRST stop MUST be at
            // 0 and the LAST at `period` (both transparent); otherwise the repeating segment
            // collapses to just the 1px line and paints the entire canvas solid — the bug that hid
            // the grid behind a flat gray fill. Layers are ordered axis-first so the world-0 axis
            // (#4D4D4D) wins where it overlaps the coincident major/minor lines, then major > minor.
            var minorPhaseX = Mod(_offsetX, spacing);
            var minorPhaseY = Mod(_offsetY, spacing);
            var majorPhaseX = Mod(_offsetX, majorStep);
            var majorPhaseY = Mod(_offsetY, majorStep);

            return $"width:{_canvasW.ToString("0")}px;height:{_canvasH.ToString("0")}px;background-color:{Background};" +
                   "background-image:" +
                   // axis at world 0 (canvas-local x = _offsetX / y = _offsetY). A plain linear-gradient
                   // continues with its LAST color past the final stop, so each axis gradient must end on a
                   // transparent stop — otherwise the single 1px line becomes a solid fill that paints the canvas gray.
                   $"linear-gradient(to right, transparent calc({_offsetX.ToString("0.##")}px - 0.6px), " +
                   $"var(--veloxdev-ac,#4D4D4D) calc({_offsetX.ToString("0.##")}px - 0.6px), " +
                   $"var(--veloxdev-ac,#4D4D4D) calc({_offsetX.ToString("0.##")}px + 0.6px), transparent calc({_offsetX.ToString("0.##")}px + 0.6px))," +
                   $"linear-gradient(to bottom, transparent calc({_offsetY.ToString("0.##")}px - 0.6px), " +
                   $"var(--veloxdev-ac,#4D4D4D) calc({_offsetY.ToString("0.##")}px - 0.6px), " +
                   $"var(--veloxdev-ac,#4D4D4D) calc({_offsetY.ToString("0.##")}px + 0.6px), transparent calc({_offsetY.ToString("0.##")}px + 0.6px))," +
                   // vertical major grid (period = majorStep = spacing * MajorLineEvery)
                   $"repeating-linear-gradient(to right, transparent 0, transparent {majorPhaseX.ToString("0.##")}px, " +
                   $"var(--veloxdev-mgc,#3A3D40) {majorPhaseX.ToString("0.##")}px, " +
                   $"var(--veloxdev-mgc,#3A3D40) calc({majorPhaseX.ToString("0.##")}px + 1px), " +
                   $"transparent calc({majorPhaseX.ToString("0.##")}px + 1px), transparent {majorStep.ToString("0.##")}px)," +
                   // horizontal major grid
                   $"repeating-linear-gradient(to bottom, transparent 0, transparent {majorPhaseY.ToString("0.##")}px, " +
                   $"var(--veloxdev-mgc,#3A3D40) {majorPhaseY.ToString("0.##")}px, " +
                   $"var(--veloxdev-mgc,#3A3D40) calc({majorPhaseY.ToString("0.##")}px + 1px), " +
                   $"transparent calc({majorPhaseY.ToString("0.##")}px + 1px), transparent {majorStep.ToString("0.##")}px)," +
                   // vertical minor grid (period = spacing)
                   $"repeating-linear-gradient(to right, transparent 0, transparent {minorPhaseX.ToString("0.##")}px, " +
                   $"var(--veloxdev-gc,#2A2D2E) {minorPhaseX.ToString("0.##")}px, " +
                   $"var(--veloxdev-gc,#2A2D2E) calc({minorPhaseX.ToString("0.##")}px + 1px), " +
                   $"transparent calc({minorPhaseX.ToString("0.##")}px + 1px), transparent {spacing.ToString("0.##")}px)," +
                   // horizontal minor grid
                   $"repeating-linear-gradient(to bottom, transparent 0, transparent {minorPhaseY.ToString("0.##")}px, " +
                   $"var(--veloxdev-gc,#2A2D2E) {minorPhaseY.ToString("0.##")}px, " +
                   $"var(--veloxdev-gc,#2A2D2E) calc({minorPhaseY.ToString("0.##")}px + 1px), " +
                   $"transparent calc({minorPhaseY.ToString("0.##")}px + 1px), transparent {spacing.ToString("0.##")}px);" +
                   $"--veloxdev-gs:{spacing.ToString("0.#")}px;" +
                   $"--veloxdev-gc:{GridColor};--veloxdev-mgc:{MajorGridColor};--veloxdev-ac:{AxisColor};";
        }
    }

    private static double Mod(double value, double mod)
    {
        var r = value % mod;
        return r < 0 ? r + mod : r;
    }

    private string ContentStyle
    {
        get
        {
            return $"position:absolute;left:{_offsetX.ToString("0")}px;top:{_offsetY.ToString("0")}px;" +
                   $"width:{ContentWidth.ToString("0")}px;height:{ContentHeight.ToString("0")}px;";
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
            _handle = await _module.InvokeAsync<IJSObjectReference>("initSurface", _scroller, _canvas, _dotNetRef);
        }
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
        if (canvasW > _canvasW)
        {
            _canvasW = canvasW;
        }

        if (canvasH > _canvasH)
        {
            _canvasH = canvasH;
        }

        if (offsetX > _offsetX)
        {
            _offsetX = offsetX;
        }

        if (offsetY > _offsetY)
        {
            _offsetY = offsetY;
        }

        if (Tree is not null)
        {
            var layout = Tree.Layout;
            var contentX = layout?.ActualOffset.Horizontal ?? 0;
            var contentY = layout?.ActualOffset.Vertical ?? 0;
            var viewportX = scrollLeft - _offsetX - contentX;
            var viewportY = scrollTop - _offsetY - contentY;
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
            InvokeAsync(StateHasChanged);
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
