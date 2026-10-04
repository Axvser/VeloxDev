namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when a pointer button goes down.</summary>
/// <remarks>
/// Core has no default reaction to a press: selecting, dragging and opening a context menu are the adapter's own
/// gesture logic, which runs after the route unless a subscriber set
/// <see cref="WorkflowEventHandle.PreventDefault"/>.
/// </remarks>
/// <seealso cref="WorkflowPointerEventArgs"/>
public sealed class WorkflowPointerPressedEventArgs : WorkflowPointerEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="position">Where the pointer is, layer included.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="source">The view the input came from.</param>
    /// <param name="target">The component under the button, or <see langword="null"/> for empty canvas.</param>
    /// <param name="button">Which button went down.</param>
    /// <param name="clickCount">How many clicks this press completes.</param>
    /// <param name="handle">The handle for this action.</param>
    public WorkflowPointerPressedEventArgs(
        Anchor position, InputModifiers modifiers, object? source, IWorkflowViewModel? target,
        WorkflowMouseButton button, int clickCount, WorkflowEventHandle handle)
        : base(position, modifiers, source, target, handle)
    {
        Button = button;
        ClickCount = clickCount;
    }

    /// <summary>Which button went down, or <see cref="WorkflowMouseButton.None"/> when the platform cannot say.</summary>
    public WorkflowMouseButton Button { get; }

    /// <summary>How many clicks this press completes — 2 for the second press of a double click.</summary>
    public int ClickCount { get; }
}
