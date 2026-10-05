namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when the pointer arrives over a component — or over empty canvas, where the target is null.</summary>
/// <seealso cref="PointerEventArgs"/>
public sealed class PointerEnteredEventArgs : PointerEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="position">Where the pointer is, layer included.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="source">The view the input came from.</param>
    /// <param name="target">The component the pointer arrived on, or <see langword="null"/> for empty canvas.</param>
    /// <param name="handle">The handle for this action.</param>
    public PointerEnteredEventArgs(
        Anchor position, InputModifiers modifiers, object? source, IWorkflowViewModel? target, WorkflowEventHandle handle)
        : base(position, modifiers, source, target, handle)
    {
    }
}
