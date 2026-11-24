using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Describes how one platform turns a wheel delta into a scroll step. Either the platform scrolls in lines
/// (<see cref="ScrollLines"/> × <see cref="LineHeight"/>) or in a fixed pixel amount
/// (<see cref="PixelsPerNotch"/>); <see cref="DeltaPerNotch"/> is the delta value the platform reports for one
/// physical notch (120 on Win32-based frameworks, 1 where the delta is already a notch count).
/// </summary>
/// <remarks>
/// The adapter reads its own system setting and fills this in; <see cref="SurfaceScrollerCore"/> owns the
/// conversion so the sign of the two axes and the zero-delta rule are written once. A profile built with
/// <see cref="FromPixels"/> ignores <see cref="ScrollLines"/> and <see cref="LineHeight"/>.
/// </remarks>
/// <seealso cref="ISurfaceScrollHost.WheelProfile"/>
public readonly struct SurfaceWheelProfile
{
    /// <summary>The wheel delta the platform reports for one physical notch; must be greater than zero.</summary>
    public double DeltaPerNotch { get; init; }

    /// <summary>Pixels scrolled per notch for a pixel-based platform; zero selects the line-based step.</summary>
    public double PixelsPerNotch { get; init; }

    /// <summary>Lines scrolled per notch; a non-positive value means "scroll by one viewport".</summary>
    public double ScrollLines { get; init; }

    /// <summary>Height of one line in the platform's scroll units.</summary>
    public double LineHeight { get; init; }

    /// <summary>Creates a profile whose step is a number of lines per notch.</summary>
    /// <param name="deltaPerNotch">The delta value one physical notch reports.</param>
    /// <param name="scrollLines">Lines per notch; non-positive means page scrolling.</param>
    /// <param name="lineHeight">Height of one line; defaults to <see cref="SurfaceScrollerCore.DefaultLineHeight"/>.</param>
    /// <returns>The profile.</returns>
    public static SurfaceWheelProfile FromLines(
        double deltaPerNotch, double scrollLines, double lineHeight = SurfaceScrollerCore.DefaultLineHeight)
        => new()
        {
            DeltaPerNotch = deltaPerNotch,
            ScrollLines = scrollLines,
            LineHeight = lineHeight,
        };

    /// <summary>Creates a profile whose step is a fixed pixel amount per notch.</summary>
    /// <param name="deltaPerNotch">The delta value one physical notch reports.</param>
    /// <param name="pixelsPerNotch">Pixels scrolled per notch.</param>
    /// <returns>The profile.</returns>
    public static SurfaceWheelProfile FromPixels(double deltaPerNotch, double pixelsPerNotch)
        => new()
        {
            DeltaPerNotch = deltaPerNotch,
            PixelsPerNotch = pixelsPerNotch,
        };

    // 零或负的 notch 会把步长变成无穷大；调用方填错时按一个 notch 处理，至少不产生 NaN。
    internal double SafeDeltaPerNotch => DeltaPerNotch > 0d ? DeltaPerNotch : 1d;
}
