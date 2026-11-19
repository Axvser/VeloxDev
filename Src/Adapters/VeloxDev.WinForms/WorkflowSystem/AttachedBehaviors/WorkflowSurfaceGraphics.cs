using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Drawing helpers shared by the surface controls and the views generated for them.
/// </summary>
public static class WorkflowSurfaceGraphics
{
    /// <summary>
    /// Builds a rounded rectangle whose corner radius is clamped to half the shorter side.
    /// </summary>
    /// <param name="bounds">The rectangle.</param>
    /// <param name="radius">The requested corner radius.</param>
    /// <returns>The path; the caller owns it.</returns>
    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        var d = 2 * r;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
