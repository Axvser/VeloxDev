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

    /// <inheritdoc />
    public double RulerBand => RulerThickness;

    // 角/带的内联样式必须带 px 单位 —— 无单位长度（如 28）是无效 CSS，浏览器会丢弃，角、带与刻度线塌成 0×0。
    private string RulerThicknessCss => Css(RulerThickness) + "px";

    // 所有进入 style 属性的长度都走这里：固定不变、且总是用 '.' 小数点，否则逗号小数文化会让浏览器整条丢弃该声明。
    private static string Css(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    // 带横跨整个表面宽/高（背景总覆盖条纹），只有刻度层平移。视口按规范坐标上报（ScrollOffset = 原始滚动偏移 >= 0，ContentOffset = 有效世界原点 = ActualOffset + 越界量），
    // 所以世界坐标 v 的物理网格/轴线位于 v + ContentOffset + RulerThickness − ScrollOffset（RulerThickness 预留只是视觉性的画布平移）。
    // 把刻度层平移这一项、在带局部 v 处画世界刻度，就正好落在物理网格线上。
    private string TopTransform => $"translateX({Css(ContentOffsetX + RulerThickness - ScrollOffsetX)}px)";
    private string LeftTransform => $"translateY({Css(ContentOffsetY + RulerThickness - ScrollOffsetY)}px)";
    private string TickLengthCss(bool isMajor)
        => Css(isMajor ? Math.Max(0, RulerThickness - 6) : Math.Max(6, RulerThickness * 0.35)) + "px";
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
