namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when a key goes down.</summary>
/// <remarks>
/// The route performs no action of its own: a host that wants Delete to delete a link subscribes here and executes
/// <c>link.DeleteCommand</c> — after checking <see cref="WorkflowEventHandle.PreventDefault"/>, so a subscriber
/// closer to the target can refuse that one press.
/// </remarks>
/// <seealso cref="KeyEventArgs"/>
public sealed class KeyDownEventArgs : KeyEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="key">The key, or <see cref="InputKey.Unknown"/>.</param>
    /// <param name="rawKeyCode">The platform's own key code.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="isRepeat">Whether the platform reports this as an auto-repeat.</param>
    /// <param name="source">The view that had the keyboard focus.</param>
    /// <param name="target">The component the adapter applies this key to.</param>
    /// <param name="handle">The handle for this action.</param>
    public KeyDownEventArgs(
        InputKey key, int rawKeyCode, InputModifiers modifiers, bool isRepeat,
        object? source, IWorkflowViewModel? target, WorkflowEventHandle handle)
        : base(key, rawKeyCode, modifiers, isRepeat, source, target, handle)
    {
    }
}
