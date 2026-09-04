using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Ruler bars (grid decorator) that mirror the scroll/content offset of a
/// <see cref="WorkflowSurfaceBehavior"/>. Consumes a <see cref="SurfaceViewport"/>
/// context and implements <see cref="IWorkflowGridDecorator"/> for API parity with the
/// XAML adapters.
/// </summary>
public partial class WorkflowGridDecorator : ComponentBase, IWorkflowGridDecorator, IDisposable
{
    /// <summary>Gets or sets the surface viewport context pushed by <see cref="WorkflowSurfaceBehavior"/>.</summary>
    [Parameter]
    public SurfaceViewport? Viewport { get; set; }

    /// <summary>
    /// Gets or sets the surface viewport feed pushed by <see cref="WorkflowSurfaceBehavior"/>. When set,
    /// the decorator subscribes and re-renders its cheap tick layer on every viewport change without
    /// dragging the surface's node/link content along.
    /// </summary>
    [CascadingParameter]
    public SurfaceViewportFeed? ViewportFeed { get; set; }

    private SurfaceViewportFeed? _subscribedFeed;

    /// <summary>Gets or sets the ruler thickness in pixels.</summary>
    [Parameter]
    public double RulerThickness { get; set; } = 28;

    /// <summary>Gets or sets the tick spacing in pixels.</summary>
    [Parameter]
    public double Spacing { get; set; } = 40;

    /// <summary>Gets or sets the ruler background color.</summary>
    [Parameter]
    public string RulerBackground { get; set; } = "rgba(37,37,38,0.78)";

    /// <summary>Gets or sets the tick color.</summary>
    [Parameter]
    public string TickColor { get; set; } = "#555555";

    /// <summary>Gets or sets the numeric label color.</summary>
    [Parameter]
    public string LabelColor { get; set; } = "#888888";

    /// <summary>Gets or sets the number of minor cells between major ticks (major ticks get labels).</summary>
    [Parameter]
    public int MajorLineEvery { get; set; } = 5;

    /// <summary>Gets or sets the ruler divider color.</summary>
    [Parameter]
    public string DividerColor { get; set; } = "#3A3D40";

    /// <summary>Gets or sets the axis (world 0) ruler tick color. Defaults to the tick color.</summary>
    [Parameter]
    public string? AxisColor { get; set; }

    /// <inheritdoc />
    public double ScrollOffsetX { get; set; }

    /// <inheritdoc />
    public double ScrollOffsetY { get; set; }

    /// <inheritdoc />
    public double ContentOffsetX { get; set; }

    /// <inheritdoc />
    public double ContentOffsetY { get; set; }

    // The corner/band inline styles need an explicit px unit — a unitless length (e.g. "28") is
    // invalid CSS and the browser drops it, collapsing the corner, bands, and tick lines to 0×0.
    private string RulerThicknessCss => RulerThickness.ToString("0.#") + "px";

    // The bands span the full surface width/height (background always covers the strip); only the
    // tick layer translates. The viewport is reported in canonical coordinates (ScrollOffset = raw
    // scroll offset >= 0, ContentOffset = effective world-origin = ActualOffset + overscroll), so the
    // physical grid/axis line for world v sits at v + ContentOffset + RulerThickness - ScrollOffset
    // (the RulerThickness reserve is a visual-only canvas translate). Translate the tick layer by that
    // term and draw a world tick at band-local v to land exactly on the physical grid line.
    private string TopTransform => $"translateX({(ContentOffsetX + RulerThickness - ScrollOffsetX).ToString("0.#")}px)";
    private string LeftTransform => $"translateY({(ContentOffsetY + RulerThickness - ScrollOffsetY).ToString("0.#")}px)";
    private string TickLengthCss(bool isMajor)
        => (isMajor ? Math.Max(0, RulerThickness - 6) : Math.Max(6, RulerThickness * 0.35)).ToString("0.#") + "px";
    private string AxisColorCss => string.IsNullOrEmpty(AxisColor) ? TickColor : AxisColor;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        if (ViewportFeed is not null)
        {
            _subscribedFeed = ViewportFeed;
            ViewportFeed.Changed += OnFeedChanged;
        }
    }

    private void OnFeedChanged(SurfaceViewport vp)
    {
        ScrollOffsetX = vp.ScrollLeft;
        ScrollOffsetY = vp.ScrollTop;
        ContentOffsetX = vp.ContentOffsetX;
        ContentOffsetY = vp.ContentOffsetY;
        InvokeAsync(StateHasChanged);
    }

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
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_subscribedFeed is not null)
        {
            _subscribedFeed.Changed -= OnFeedChanged;
            _subscribedFeed = null;
        }
    }

    /// <summary>
    /// Tick positions along the horizontal (top) ruler, in band-local pixels. The band is
    /// translated by -ScrollOffsetX, and world <c>v</c> sits at band-local <c>v</c> (world 0 lands
    /// on the ruler/content boundary), mirroring the XAML adapters' content translation. Emits the
    /// viewport-visible range plus one spacing of margin so edges stay covered while scrolling.
    /// </summary>
    private IEnumerable<(double Pos, bool IsMajor, bool IsZero)> TopTicks
        => ComputeTicks(ScrollOffsetX - ContentOffsetX - RulerThickness, Viewport?.ViewportWidth ?? 0);

    /// <summary>
    /// Tick positions along the vertical (left) ruler, in band-local pixels. See <see cref="TopTicks"/>.
    /// </summary>
    private IEnumerable<(double Pos, bool IsMajor, bool IsZero)> LeftTicks
        => ComputeTicks(ScrollOffsetY - ContentOffsetY - RulerThickness, Viewport?.ViewportHeight ?? 0);

    private IEnumerable<(double Pos, bool IsMajor, bool IsZero)> ComputeTicks(double rangeStart, double extent)
    {
        var spacing = Math.Max(8, Spacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);

        var first = WorkflowSurfaceMath.GridFirstLine(rangeStart, spacing);
        for (var v = first; v <= rangeStart + extent + spacing; v += spacing)
        {
            var isZero = Math.Abs(v) < 0.001;
            var isMajor = isZero
                          || Math.Abs(v % majorStep) < 0.001
                          || Math.Abs(v % majorStep - majorStep) < 0.001
                          || Math.Abs(v % majorStep + majorStep) < 0.001;
            yield return (v, isMajor, isZero);
        }
    }

    private static string FormatGridValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 10000)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        if (abs < 1000000)
        {
            return Math.Round(value / 1000d, 1).ToString(CultureInfo.InvariantCulture) + "K";
        }

        return Math.Round(value / 1000000d, 1).ToString(CultureInfo.InvariantCulture) + "M";
    }
}
