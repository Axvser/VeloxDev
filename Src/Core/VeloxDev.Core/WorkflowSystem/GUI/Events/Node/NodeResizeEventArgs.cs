using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a node resize: the size it had and the size it is going to.
/// </summary>
/// <remarks>
/// Raised twice for one resize: <c>Resizing</c> before the framework applies it — where
/// <see cref="WorkflowEventHandle.PreventDefault"/> keeps the old size — and <c>Resized</c> after, carrying the
/// same handle.
/// </remarks>
public sealed class NodeResizeEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="node">The node being resized.</param>
    /// <param name="from">Its current size.</param>
    /// <param name="to">The size it is going to.</param>
    /// <param name="handle">The handle for this action — the same instance the other phase carries.</param>
    public NodeResizeEventArgs(IWorkflowNodeViewModel node, Size from, Size to, WorkflowEventHandle handle)
    {
        Node = node;
        From = from;
        To = to;
        Handle = handle;
    }

    /// <summary>The node being resized.</summary>
    public IWorkflowNodeViewModel Node { get; }

    /// <summary>Its current size.</summary>
    public Size From { get; }

    /// <summary>The size it is going to.</summary>
    public Size To { get; }

    /// <summary>Veto or silence the resize.</summary>
    public WorkflowEventHandle Handle { get; }
}
