namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when the wheel turns over the surface.</summary>
/// <remarks>
/// The wheel is its own event rather than a <see cref="MouseButton"/> value, mirroring Avalonia's
/// <c>PointerWheelChanged</c>. Core has no default reaction: zooming stays the adapter's, so a subscriber sees this
/// event in addition to whatever the surface does with the wheel.
/// </remarks>
/// <seealso cref="PointerEventArgs"/>
public sealed class PointerWheelEventArgs : PointerEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="position">Where the pointer is, layer included.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="source">The view the input came from.</param>
    /// <param name="target">The component under the pointer, or <see langword="null"/> for empty canvas.</param>
    /// <param name="deltaX">Horizontal wheel movement, in the platform's own units.</param>
    /// <param name="deltaY">Vertical wheel movement; positive is away from the user.</param>
    /// <param name="handle">The handle for this action.</param>
    public PointerWheelEventArgs(
        Anchor position, InputModifiers modifiers, object? source, IWorkflowViewModel? target,
        double deltaX, double deltaY, WorkflowEventHandle handle)
        : base(position, modifiers, source, target, handle)
    {
        DeltaX = deltaX;
        DeltaY = deltaY;
    }

    /// <summary>Horizontal wheel movement, in the platform's own units.</summary>
    public double DeltaX { get; }

    /// <summary>Vertical wheel movement; positive is away from the user.</summary>
    public double DeltaY { get; }
}
