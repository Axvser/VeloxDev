namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when the pointer leaves a component — or leaves the surface, where the target is null.</summary>
/// <remarks>
/// A subscription that tracks the pointer must honour this event even while a host's own popup is open, which is
/// what <see cref="WorkflowInput.IsSuspended"/> exists for.
/// </remarks>
/// <seealso cref="PointerEventArgs"/>
public sealed class PointerExitedEventArgs : PointerEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="position">Where the pointer is, layer included.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="source">The view the input came from.</param>
    /// <param name="target">The component the pointer left, or <see langword="null"/> for empty canvas.</param>
    /// <param name="handle">The handle for this action.</param>
    public PointerExitedEventArgs(
        Anchor position, InputModifiers modifiers, object? source, IWorkflowViewModel? target, WorkflowEventHandle handle)
        : base(position, modifiers, source, target, handle)
    {
    }
}
