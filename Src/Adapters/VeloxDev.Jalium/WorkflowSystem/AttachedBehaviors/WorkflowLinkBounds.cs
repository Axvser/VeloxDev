using System;
using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Controls;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Grows and moves a self-drawing element's layout box so that it covers everything it draws.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one deliberate divergence from the XAML adapters, and it exists because of a Jalium hard limit.</b>
/// Jalium's renderer culls a child entirely when its layout box misses the viewport clip and never looks at the
/// drawn content (<c>Visual.ShouldRenderChild</c>; see
/// <c>memory/modules/WorkflowSystem/adapters/jalium.md</c> §2.1). A link that draws a curve outside its box is
/// therefore not clipped — it is <b>dropped silently</b>, and the symptom is deep-zoom links vanishing.
/// </para>
/// <para>
/// So a link view must first move its own box onto the curve and only then draw. Because the box moves, the
/// coordinates the view draws with must be expressed relative to it — hence the two <c>origin</c> outputs:
/// subtract them from the canvas-local points and the visual output is identical to drawing at the canvas origin.
/// </para>
/// <para>
/// The box is what it is for; it is not a hit target. Hit testing goes through the curve the view publishes to
/// Core, never through this box.
/// </para>
/// </remarks>
public static class WorkflowLinkBounds
{
    /// <summary>How far the box is grown past the drawn extent, in canvas units.</summary>
    public const double BoxPad = 6d;

    /// <summary>
    /// Moves <paramref name="view"/>'s layout box onto the bounding box of <paramref name="canvasLocalPoints"/>.
    /// </summary>
    /// <param name="view">The self-drawing element; its parent must be a <see cref="Canvas"/>.</param>
    /// <param name="canvasLocalPoints">The points to cover, in the coordinate space of the host panel.</param>
    /// <param name="originX">Receives the box's left edge, to subtract from the same points when drawing.</param>
    /// <param name="originY">Receives the box's top edge, to subtract from the same points when drawing.</param>
    /// <returns><see langword="true"/> if a box was applied; <see langword="false"/> if there was nothing to cover.</returns>
    public static bool Apply(
        FrameworkElement view,
        IReadOnlyList<Point> canvasLocalPoints,
        out double originX,
        out double originY)
    {
        originX = 0d;
        originY = 0d;

        if (canvasLocalPoints.Count == 0)
        {
            return false;
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var point in canvasLocalPoints)
        {
            if (point.X < minX) minX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.X > maxX) maxX = point.X;
            if (point.Y > maxY) maxY = point.Y;
        }

        var left = minX - BoxPad;
        var top = minY - BoxPad;
        var right = maxX + BoxPad;
        var bottom = maxY + BoxPad;

        originX = left;
        originY = top;

        Canvas.SetLeft(view, left);
        Canvas.SetTop(view, top);
        view.Width = Math.Max(1d, right - left);
        view.Height = Math.Max(1d, bottom - top);

        return true;
    }
}
