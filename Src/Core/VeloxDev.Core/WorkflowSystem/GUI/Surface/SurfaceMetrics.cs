using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A snapshot of the measurements a workflow surface uses to place its overlays: the visible viewport, the
/// canvas content offset, the ruler band and scrollbar thicknesses that shrink the usable corners, and the
/// resulting available right/top/bottom edges.
/// </summary>
/// <remarks>
/// <para>
/// The surface behavior computes this from the platform's scroll container and decorators, then pushes the
/// values to the grid decorator, the minimap overlay and the tree's viewport through
/// <see cref="SurfaceMetricsPush"/>. Keeping the arithmetic here means every adapter shrinks the overlay area
/// for its rulers and scrollbars the same way instead of repeating the subtraction.
/// </para>
/// <para>
/// Scroll offset and content offset are canvas-local inputs, not pixels owned by any one platform. The
/// viewport edges follow <see cref="WorkflowSurfaceMath.ToCanvasLocal"/>: <c>local = scroll − contentOffset</c>.
/// </para>
/// </remarks>
/// <seealso cref="ISurfaceMetricsSource"/>
/// <seealso cref="SurfaceMetricsPush"/>
public readonly struct SurfaceMetrics
{
    /// <summary>Horizontal scroll offset of the surface's scroll container.</summary>
    public double ScrollOffsetX { get; init; }

    /// <summary>Vertical scroll offset of the surface's scroll container.</summary>
    public double ScrollOffsetY { get; init; }

    /// <summary>Horizontal canvas content offset (<c>CanvasLayout.ActualOffset.Horizontal</c>).</summary>
    public double ContentOffsetX { get; init; }

    /// <summary>Vertical canvas content offset (<c>CanvasLayout.ActualOffset.Vertical</c>).</summary>
    public double ContentOffsetY { get; init; }

    /// <summary>Width of the visible viewport.</summary>
    public double ViewportWidth { get; init; }

    /// <summary>Height of the visible viewport.</summary>
    public double ViewportHeight { get; init; }

    /// <summary>Thickness of the ruler/scale band that overlaps the top and left canvas edges.</summary>
    public double RulerBand { get; init; }

    /// <summary>Width of the vertical scrollbar when it is visible, otherwise zero.</summary>
    public double VerticalScrollBarThickness { get; init; }

    /// <summary>Height of the horizontal scrollbar when it is visible, otherwise zero.</summary>
    public double HorizontalScrollBarThickness { get; init; }

    /// <summary>The free corner overlays may use, derived from the surface size, ruler and scrollbars.</summary>
    public SurfaceAvailableArea Available { get; init; }

    /// <summary>
    /// Creates a metrics snapshot and derives the three available edges from the surface size, the ruler band
    /// and the scrollbar thicknesses.
    /// </summary>
    /// <param name="scrollOffsetX">Horizontal scroll offset.</param>
    /// <param name="scrollOffsetY">Vertical scroll offset.</param>
    /// <param name="contentOffsetX">Horizontal canvas content offset.</param>
    /// <param name="contentOffsetY">Vertical canvas content offset.</param>
    /// <param name="viewportWidth">Visible viewport width.</param>
    /// <param name="viewportHeight">Visible viewport height.</param>
    /// <param name="surfaceWidth">Width of the surface that hosts the overlays.</param>
    /// <param name="surfaceHeight">Height of the surface that hosts the overlays.</param>
    /// <param name="rulerBand">Ruler band thickness overlapping the top and left edges.</param>
    /// <param name="verticalScrollBarThickness">Vertical scrollbar width, or zero when hidden.</param>
    /// <param name="horizontalScrollBarThickness">Horizontal scrollbar height, or zero when hidden.</param>
    /// <param name="margin">Gap an overlay keeps from the available corner.</param>
    /// <returns>The populated snapshot.</returns>
    public static SurfaceMetrics Create(
        double scrollOffsetX,
        double scrollOffsetY,
        double contentOffsetX,
        double contentOffsetY,
        double viewportWidth,
        double viewportHeight,
        double surfaceWidth,
        double surfaceHeight,
        double rulerBand,
        double verticalScrollBarThickness,
        double horizontalScrollBarThickness,
        double margin)
        => new()
        {
            ScrollOffsetX = scrollOffsetX,
            ScrollOffsetY = scrollOffsetY,
            ContentOffsetX = contentOffsetX,
            ContentOffsetY = contentOffsetY,
            ViewportWidth = viewportWidth,
            ViewportHeight = viewportHeight,
            RulerBand = rulerBand,
            VerticalScrollBarThickness = verticalScrollBarThickness,
            HorizontalScrollBarThickness = horizontalScrollBarThickness,
            Available = new SurfaceAvailableArea
            {
                Right = Math.Max(0d, surfaceWidth - verticalScrollBarThickness),
                Top = Math.Max(0d, rulerBand),
                Bottom = Math.Max(0d, surfaceHeight - horizontalScrollBarThickness),
                Margin = margin,
            },
        };
}
