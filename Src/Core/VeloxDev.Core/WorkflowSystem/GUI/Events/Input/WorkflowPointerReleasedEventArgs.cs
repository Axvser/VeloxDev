namespace VeloxDev.WorkflowSystem;

/// <summary>Raised when a pointer button comes up, including the release that ends a drag.</summary>
/// <remarks>
/// Like a press, this has no default reaction in Core — it is what an adapter's drag bookkeeping listens to.
/// </remarks>
/// <seealso cref="WorkflowPointerEventArgs"/>
public sealed class WorkflowPointerReleasedEventArgs : WorkflowPointerEventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="position">Where the pointer is, layer included.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="source">The view the input came from.</param>
    /// <param name="target">The component under the pointer, or <see langword="null"/> for empty canvas.</param>
    /// <param name="button">Which button came up.</param>
    /// <param name="clickCount">How many clicks the gesture that just ended had.</param>
    /// <param name="handle">The handle for this action.</param>
    public WorkflowPointerReleasedEventArgs(
        Anchor position, InputModifiers modifiers, object? source, IWorkflowViewModel? target,
        WorkflowMouseButton button, int clickCount, WorkflowEventHandle handle)
        : base(position, modifiers, source, target, handle)
    {
        Button = button;
        ClickCount = clickCount;
    }

    /// <summary>Which button came up, or <see cref="WorkflowMouseButton.None"/> when the platform cannot say.</summary>
    public WorkflowMouseButton Button { get; }

    /// <summary>How many clicks the gesture that just ended had.</summary>
    public int ClickCount { get; }
}
