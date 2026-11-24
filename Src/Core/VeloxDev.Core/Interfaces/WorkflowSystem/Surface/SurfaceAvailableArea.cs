using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The free top-right area a surface offers to its overlays, already clear of the ruler band and the
/// scrollbars, plus the gap an overlay keeps from that corner.
/// </summary>
/// <remarks>
/// Both the surface metrics and the minimap metrics carry this value, so the subtraction for rulers and
/// scrollbars and the resulting placement are computed once instead of once per pipeline.
/// </remarks>
public readonly struct SurfaceAvailableArea : IEquatable<SurfaceAvailableArea>
{
    /// <summary>The x of the usable right edge.</summary>
    public double Right { get; init; }

    /// <summary>The y of the usable top edge, below the horizontal ruler band.</summary>
    public double Top { get; init; }

    /// <summary>The y of the usable bottom edge, above the horizontal scrollbar.</summary>
    public double Bottom { get; init; }

    /// <summary>The gap kept between an overlay and this corner.</summary>
    public double Margin { get; init; }

    /// <summary>Whether the area has a positive width and height.</summary>
    public bool IsUsable => Right > 0 && Bottom > Top;

    /// <summary>
    /// Places a box of the requested size inside the corner: its top-right corner sits at
    /// (<see cref="Right"/> - margin, <see cref="Top"/> + margin), and the size is clamped to the free
    /// area and <paramref name="minimumSize"/>. Falls back to the origin when the area is unusable.
    /// </summary>
    /// <param name="desiredWidth">The requested width.</param>
    /// <param name="desiredHeight">The requested height.</param>
    /// <param name="minimumSize">The smallest width or height the box may shrink to.</param>
    public WorkflowBounds GetRect(double desiredWidth, double desiredHeight, double minimumSize)
    {
        var min = Math.Max(1, minimumSize);
        var margin = Math.Max(0, Margin);
        var width = Math.Max(min, desiredWidth);
        var height = Math.Max(min, desiredHeight);

        if (!IsUsable)
        {
            return WorkflowBounds.FromNode(0, 0, width, height);
        }

        width = Math.Min(width, Math.Max(1, Right - margin * 2));
        height = Math.Min(height, Math.Max(1, Bottom - Top - margin * 2));
        var left = Math.Max(margin, Right - margin - width);
        var top = Top + margin;
        return WorkflowBounds.FromNode(left, top, width, height);
    }

    /// <inheritdoc />
    public bool Equals(SurfaceAvailableArea other)
        => Right.Equals(other.Right)
            && Top.Equals(other.Top)
            && Bottom.Equals(other.Bottom)
            && Margin.Equals(other.Margin);
}
