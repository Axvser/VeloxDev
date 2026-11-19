using System;
using System.Drawing;
using System.Globalization;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Grid-line classification and ruler-label formatting, shared by every WinForms surface that draws a grid.
/// </summary>
/// <remarks>
/// These were written out privately in each of them — the surface canvas, the floating ruler overlay and the
/// standalone grid decorator — so a change to the "what counts as a major line" rule had three places to miss.
/// </remarks>
public static class WorkflowSurfaceGrid
{
    /// <summary>How close to a multiple of the major step a coordinate has to be to count as a major line.</summary>
    public const double MajorLineEpsilon = 0.001;

    /// <summary>Whether a world coordinate falls on a major (heavier) grid line.</summary>
    /// <param name="value">The world coordinate.</param>
    /// <param name="majorStep">The world distance between major lines.</param>
    /// <returns><see langword="true"/> when the line is major.</returns>
    public static bool IsMajorLine(double value, double majorStep)
        => majorStep > 0
            && (Math.Abs(value % majorStep) < MajorLineEpsilon
                || Math.Abs(value % majorStep - majorStep) < MajorLineEpsilon
                || Math.Abs(value % majorStep + majorStep) < MajorLineEpsilon);

    /// <summary>Whether a world coordinate is the axis (zero).</summary>
    /// <param name="value">The world coordinate.</param>
    /// <returns><see langword="true"/> when the coordinate is zero within the epsilon.</returns>
    public static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;

    /// <summary>Renders a ruler label for a world coordinate, abbreviating thousands and millions.</summary>
    /// <param name="value">The world coordinate.</param>
    /// <returns>The label.</returns>
    public static string FormatGridValue(double value)
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

    /// <summary>Picks the pen for a grid line: the axis pen at zero, the major pen on a major line, else the minor one.</summary>
    /// <param name="value">The world coordinate.</param>
    /// <param name="majorStep">The world distance between major lines.</param>
    /// <param name="minorPen">The minor pen.</param>
    /// <param name="majorPen">The major pen.</param>
    /// <param name="axisPen">The axis pen.</param>
    /// <returns>The pen to draw that line with.</returns>
    public static Pen SelectPen(double value, double majorStep, Pen minorPen, Pen majorPen, Pen axisPen)
        => IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;
}
