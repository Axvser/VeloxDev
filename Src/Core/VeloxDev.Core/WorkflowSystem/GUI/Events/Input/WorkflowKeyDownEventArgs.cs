namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when a key goes down.</summary>
/// <remarks>
/// This is the only input event with a default reaction in Core: a <see cref="WorkflowKey.Delete"/> whose target is
/// a link deletes it, unless <see cref="WorkflowEventHandle.PreventDefault"/> was set or
/// <see cref="WorkflowInput.AutoDelete"/> is off.
/// </remarks>
/// <seealso cref="WorkflowKeyEventArgs"/>
public sealed class WorkflowKeyDownEventArgs : WorkflowKeyEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="key">The key, or <see cref="WorkflowKey.Unknown"/>.</param>
    /// <param name="rawKeyCode">The platform's own key code.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="isRepeat">Whether the platform reports this as an auto-repeat.</param>
    /// <param name="source">The view that had the keyboard focus.</param>
    /// <param name="target">The component the adapter applies this key to.</param>
    /// <param name="handle">The handle for this action.</param>
    public WorkflowKeyDownEventArgs(
        WorkflowKey key, int rawKeyCode, InputModifiers modifiers, bool isRepeat,
        object? source, IWorkflowViewModel? target, WorkflowEventHandle handle)
        : base(key, rawKeyCode, modifiers, isRepeat, source, target, handle)
    {
    }
}
