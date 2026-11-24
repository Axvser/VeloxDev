namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer <see cref="SurfaceScrollerCore"/> drives: scroll offsets and limits, the wheel step,
/// and the two actions only the adapter can perform — writing an offset and letting the platform relayout a
/// grown canvas.
/// </summary>
/// <remarks>
/// Everything arithmetic (wheel conversion, clamping, overscroll growth, centering) stays in Core. A host
/// only reports what its scroll container currently is and applies the result. Offsets are in the platform's
/// scroll units; the two axes are independent, so a host may implement <see cref="ApplyOffset"/> by writing
/// only the axis it was given.
/// </remarks>
/// <seealso cref="SurfaceScrollerCore"/>
/// <seealso cref="SurfaceWheelProfile"/>
public interface ISurfaceScrollHost
{
    /// <summary>
    /// The canvas geometry the surface scrolls over, or <see langword="null"/> when no tree is bound. A
    /// <see langword="null"/> layout makes the scroller fall back to clamping at the native limits without
    /// expanding the canvas.
    /// </summary>
    CanvasLayout? Layout { get; }

    /// <summary>Width of the visible viewport.</summary>
    double ViewportWidth { get; }

    /// <summary>Height of the visible viewport.</summary>
    double ViewportHeight { get; }

    /// <summary>Current horizontal scroll offset.</summary>
    double HorizontalOffset { get; }

    /// <summary>Current vertical scroll offset.</summary>
    double VerticalOffset { get; }

    /// <summary>Largest horizontal offset the platform currently reports.</summary>
    double HorizontalMaximum { get; }

    /// <summary>Largest vertical offset the platform currently reports.</summary>
    double VerticalMaximum { get; }

    /// <summary>How this platform converts a wheel delta into a scroll step.</summary>
    SurfaceWheelProfile WheelProfile { get; }

    /// <summary>
    /// Writes the given scroll offsets. A <see langword="null"/> argument leaves that axis untouched, which is
    /// how a wheel turn that only moves one axis avoids clamping the other one back into range.
    /// </summary>
    /// <param name="horizontalOffset">The new horizontal offset, or <see langword="null"/> to leave it alone.</param>
    /// <param name="verticalOffset">The new vertical offset, or <see langword="null"/> to leave it alone.</param>
    void ApplyOffset(double? horizontalOffset, double? verticalOffset);

    /// <summary>
    /// Lets the platform adopt a canvas the scroller grew through
    /// <see cref="WorkflowSurfaceMath.ClampScrollOffset"/>: reapply the layout transform and run whatever
    /// layout pass the platform needs before <see cref="HorizontalMaximum"/> and <see cref="VerticalMaximum"/>
    /// are read again.
    /// </summary>
    void ExpandCanvas();
}
