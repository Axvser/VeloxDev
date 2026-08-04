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
            var spacingCss = GridSpacing.ToString("0.#");
            return $"width:{_canvasW.ToString("0")}px;height:{_canvasH.ToString("0")}px;background-color:{Background};" +
                   "background-image:repeating-linear-gradient(to right, transparent calc(var(--veloxdev-gs,40px) - 1px), " +
                   "var(--veloxdev-gc,#2A2D2E) calc(var(--veloxdev-gs,40px) - 1px), var(--veloxdev-gc,#2A2D2E) var(--veloxdev-gs,40px))," +
                   "repeating-linear-gradient(to bottom, transparent calc(var(--veloxdev-gs,40px) - 1px), " +
                   "var(--veloxdev-gc,#2A2D2E) calc(var(--veloxdev-gs,40px) - 1px), var(--veloxdev-gc,#2A2D2E) var(--veloxdev-gs,40px));" +
                   $"--veloxdev-gs:{spacingCss}px;--veloxdev-gc:{GridColor};";
        }
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
