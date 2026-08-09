using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Minimap overlay that renders a thumbnail overview of all nodes plus the visible viewport
/// rectangle. Consumes a <see cref="SurfaceViewport"/> context and implements
/// <see cref="IWorkflowMinimapOverlay"/> for API parity with the XAML adapters.
/// Click to jump; drag to pan the surface (via <see cref="ScrollViewerId"/>).
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

    // Content-fit mapping used to render the minimap. It is pushed to JS so drag/click navigation
    // inverts the SAME mapping (instead of a raw scroll-extent ratio, which diverges once the canvas
    // is edge-extended and overshoots by n×). Only the fields below change with content bounds.
    private double _scale;
    private double _mapOx;
    private double _mapOy;
    private double _minX;
    private double _minY;

    private IReadOnlyList<Mapped>? MappedNodes { get; set; }
    private Mapped? MappedViewport { get; set; }

    // Unitless lengths are invalid inside a CSS style="" attribute (the browser drops them,
    // collapsing the element to 0×0). These feed the inline width/height style; the px suffix is
    // also accepted by the SVG width/height attributes below.
    private string WidthCss => Width.ToString("0.#") + "px";
    private string HeightCss => Height.ToString("0.#") + "px";
    private string NodeRadiusCss => NodeRadius.ToString("0.#");
    private string ViewportStrokeWidthCss => ViewportStrokeWidth.ToString("0.#");

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (Viewport is { } vp)
        {
            ScrollOffsetX = vp.ScrollLeft;
            ScrollOffsetY = vp.ScrollTop;
            ContentOffsetX = vp.ContentOffsetX;
            ContentOffsetY = vp.ContentOffsetY;
            ViewportWidth = vp.ViewportWidth;
            ViewportHeight = vp.ViewportHeight;
            WorkflowTree = vp.Tree;
        }

        Recompute();
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
    }

    private void Recompute()
    {
        var tree = WorkflowTree;
        if (tree is null)
        {
            MappedNodes = null;
            MappedViewport = null;
            return;
        }

        var nodes = tree.Nodes?.ToArray() ?? Array.Empty<IWorkflowNodeViewModel>();
        if (nodes.Length == 0)
        {
            MappedNodes = Array.Empty<Mapped>();
            MappedViewport = null;
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
        var scale = Math.Min(drawW / bw, drawH / bh);
        var ox = pad + (drawW - bw * scale) / 2;
        var oy = pad + (drawH - bh * scale) / 2;

        _scale = scale;
        _mapOx = ox;
        _mapOy = oy;
        _minX = minX;
        _minY = minY;
        PushMapping();

        MappedNodes = nodes.Select(n =>
        {
            var x = ox + (n.Anchor.Horizontal - minX) * scale;
            var y = oy + (n.Anchor.Vertical - minY) * scale;
            return new Mapped(x, y, Math.Max(1, n.Size.Width * scale), Math.Max(1, n.Size.Height * scale));
        }).ToArray();

        var vw = Math.Max(1, ViewportWidth);
        var vh = Math.Max(1, ViewportHeight);
        var vx = ScrollOffsetX - ContentOffsetX;
        var vy = ScrollOffsetY - ContentOffsetY;
        var mx = ox + (vx - minX) * scale;
        var my = oy + (vy - minY) * scale;
        var mw = Math.Max(2, vw * scale);
        var mh = Math.Max(2, vh * scale);
        // Clamp the viewport rect inside the minimap so the draggable handle never leaves the
        // bounds. When the user pushes it past an edge, the surface's edge-expansion takes over.
        mw = Math.Min(mw, Width);
        mh = Math.Min(mh, Height);
        mx = Math.Max(0, Math.Min(mx, Width - mw));
        my = Math.Max(0, Math.Min(my, Height - mh));
        MappedViewport = new Mapped(mx, my, mw, mh);
    }

    /// <summary>
    /// Pushes the content-fit mapping used to render the minimap to JS, so drag/click
    /// navigation inverts the SAME scale instead of a raw scroll-extent ratio (which diverges
    /// once the canvas is edge-extended and overshoots by n×). Called on every Recompute and
    /// once more on first render so JS has a mapping before the user can interact.
    /// </summary>
    private void PushMapping()
    {
        if (_module is not null && !string.IsNullOrWhiteSpace(ScrollViewerId))
        {
            _ = _module.InvokeVoidAsync("setMinimapMapping", ScrollViewerId, _scale, _mapOx, _mapOy, _minX, _minY);
        }
    }

    [JSInvokable]
    public void OnMinimapNavigate(double x, double y, double currentScrollLeft, double currentScrollTop)
    {
        if (string.IsNullOrWhiteSpace(ScrollViewerId) || _module is null || _scale <= 0)
        {
            return;
        }

        // Invert the render mapping (minimapX = ox + (worldX - minX) * scale) to recover the world
        // coordinate under the click, then convert world -> scrollLeft. scrollLeft = worldX + _offsetX
        // + contentX, and _offsetX + contentX = currentScrollLeft - ScrollOffsetX (from the surface
        // viewport: ScrollOffsetX = scrollLeft - _offsetX, minus contentX). Keeping both directions on
        // the rendered scale means a drag/click tracks the viewport rectangle 1:1 even after the
        // canvas edge-extension grows the scroll extent.
        var offsetX = currentScrollLeft - ScrollOffsetX;
        var offsetY = currentScrollTop - ScrollOffsetY;
        var worldX = _minX + (x - _mapOx) / _scale;
        var worldY = _minY + (y - _mapOy) / _scale;
        var targetX = worldX + offsetX + ContentOffsetX;
        var targetY = worldY + offsetY + ContentOffsetY;

        _ = JS.InvokeVoidAsync("veloxdevWorkflow.scrollToPosition", ScrollViewerId, targetX, targetY);
    }

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

    private sealed record Mapped(double X, double Y, double W, double H)
    {
        public string XCss => X.ToString("0.#");
        public string YCss => Y.ToString("0.#");
        public string WCss => W.ToString("0.#");
        public string HCss => H.ToString("0.#");
    }
}
