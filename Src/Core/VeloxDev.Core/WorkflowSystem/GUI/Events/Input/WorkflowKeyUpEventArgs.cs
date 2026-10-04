namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when a key comes up.</summary>
/// <remarks>Core has no default reaction to a key going up; it is what a host's own gesture bookkeeping needs.</remarks>
/// <seealso cref="WorkflowKeyEventArgs"/>
public sealed class WorkflowKeyUpEventArgs : WorkflowKeyEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="key">The key, or <see cref="WorkflowKey.Unknown"/>.</param>
    /// <param name="rawKeyCode">The platform's own key code.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="isRepeat">Whether the platform reports this as an auto-repeat.</param>
    /// <param name="source">The view that had the keyboard focus.</param>
    /// <param name="target">The component the adapter applies this key to.</param>
    /// <param name="handle">The handle for this action.</param>
    public WorkflowKeyUpEventArgs(
        WorkflowKey key, int rawKeyCode, InputModifiers modifiers, bool isRepeat,
        object? source, IWorkflowViewModel? target, WorkflowEventHandle handle)
        : base(key, rawKeyCode, modifiers, isRepeat, source, target, handle)
    {
    }
}
