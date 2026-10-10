namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Scrolls the surface the input route belongs to. An adapter implements it; a host calls it after taking a wheel
/// over from the framework.
/// </summary>
/// <remarks>
/// <para>
/// The canvas wheel is the adapter's own: it keeps the platform's scroll container from scrolling the workflow
/// surface and drives that scroll itself, so <see cref="WorkflowEventHandle.PreventDefault"/> on a
/// <see cref="PointerWheelEventArgs"/> really does mean "this turn does not scroll". A subscriber that sets it has
/// taken the wheel over — and this is how it then scrolls, without knowing which platform it is on. Seven GUI
/// frameworks scroll in seven different ways (a <c>ScrollViewer</c> offset, a DOM <c>scrollBy</c>, a translated
/// canvas) and size a wheel notch in seven different ways; a host that wants the same behaviour on all of them asks
/// here instead of branching per platform.
/// </para>
/// <para>
/// The deltas are <b>wheel deltas</b>, the same values <see cref="PointerWheelEventArgs"/> carries — not scroll
/// offsets, and not a count of notches. A caller forwards what it received; turning that into this platform's
/// scroll step (a line count, a fixed pixel amount, a translated canvas) is the adapter's job, because it is the
/// adapter that knows what the platform would have done with the same wheel. That is what keeps one subscriber
/// looking the same on all seven.
/// </para>
/// <para>
/// Reach it through <see cref="WorkflowInput.Scroller"/>. It is <see langword="null"/> until an adapter registers
/// one, so a host that scrolls from a subscriber should treat it as optional.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowInput"/>
/// <seealso cref="PointerWheelEventArgs"/>
public interface IWorkflowSurfaceScroller
{
    /// <summary>Scrolls the surface as though the wheel had turned by <paramref name="wheelDeltaX"/> and
    /// <paramref name="wheelDeltaY"/>.</summary>
    /// <param name="wheelDeltaX">Horizontal wheel movement, on the same scale and with the same sign as
    /// <see cref="PointerWheelEventArgs.DeltaX"/>.</param>
    /// <param name="wheelDeltaY">Vertical wheel movement, on the same scale and with the same sign as
    /// <see cref="PointerWheelEventArgs.DeltaY"/>.</param>
    /// <remarks>
    /// Implementations clamp to the surface's own extent, so a caller may pass the raw delta without consulting the
    /// scrollable range first. A zero delta for one axis leaves that axis alone.
    /// </remarks>
    void ScrollBy(double wheelDeltaX, double wheelDeltaY);
}
