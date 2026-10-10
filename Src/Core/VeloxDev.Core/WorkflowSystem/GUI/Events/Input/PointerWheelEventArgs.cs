namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when the wheel turns over the surface.</summary>
/// <remarks>
/// <para>
/// The wheel is its own event rather than a <see cref="MouseButton"/> value, mirroring Avalonia's
/// <c>PointerWheelChanged</c>. Ctrl + wheel is the adapter's zoom gesture; a plain wheel is the surface scroll,
/// which the adapter applies itself rather than leaving to the platform's scroll container.
/// </para>
/// <para>
/// Core has no reaction of its own — the adapter reads the verdict once the route returns. Left alone it applies
/// the default vertical scroll, which is what keeps an untouched surface behaving as it always did. A subscriber
/// that sets <see cref="WorkflowEventHandle.PreventDefault"/> has taken this wheel over, and scrolls through
/// <see cref="WorkflowInput.Scroller"/>.
/// </para>
/// </remarks>
/// <seealso cref="PointerEventArgs"/>
public sealed class PointerWheelEventArgs : PointerEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="position">Where the pointer is, layer included.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="source">The view the input came from.</param>
    /// <param name="target">The component under the pointer, or <see langword="null"/> for empty canvas.</param>
    /// <param name="deltaX">Horizontal wheel movement, positive towards the right.</param>
    /// <param name="deltaY">Vertical wheel movement, positive away from the user.</param>
    /// <param name="handle">The handle for this action.</param>
    public PointerWheelEventArgs(
        Anchor position, InputModifiers modifiers, object? source, IWorkflowViewModel? target,
        double deltaX, double deltaY, WorkflowEventHandle handle)
        : base(position, modifiers, source, target, handle)
    {
        DeltaX = deltaX;
        DeltaY = deltaY;
    }

    /// <summary>
    /// Horizontal wheel movement, positive towards the right; <c>0</c> on a mouse that has no tilt wheel.
    /// </summary>
    /// <remarks>
    /// Same scale as <see cref="DeltaY"/> — a notch is one unit if the platform counts notches, or whatever the
    /// platform reports if it counts something else. Only the sign is contractual.
    /// </remarks>
    public double DeltaX { get; }

    /// <summary>Vertical wheel movement, positive away from the user (scrolling up).</summary>
    /// <remarks>
    /// The sign is normalised to that, and it is the one thing about a wheel that is <b>not</b> portable: browsers
    /// report a downward scroll as a positive <c>deltaY</c> while the desktop frameworks report a notch away from
    /// the user as positive. An adapter therefore passes the platform value through only when the platform already
    /// agrees, and negates it otherwise.
    /// </remarks>
    public double DeltaY { get; }
}
