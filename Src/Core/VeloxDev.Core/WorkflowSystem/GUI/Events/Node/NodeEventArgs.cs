using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a node whose lifetime is the subject: raised as <c>Deleting</c> before the node is torn down, and as
/// <c>Deleted</c> after, carrying the same handle.
/// </summary>
/// <remarks>
/// <see cref="WorkflowEventHandle.PreventDefault"/> on <c>Deleting</c> keeps the node in the tree. A host that
/// confirms first refuses there and deletes the node itself afterwards.
/// </remarks>
public sealed class NodeEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="node">The node the action is about.</param>
    /// <param name="handle">The handle for this action — the same instance the other phase carries.</param>
    public NodeEventArgs(IWorkflowNodeViewModel node, WorkflowEventHandle handle)
    {
        Node = node;
        Handle = handle;
    }

    /// <summary>The node the action is about.</summary>
    public IWorkflowNodeViewModel Node { get; }

    /// <summary>Veto or silence the action.</summary>
    public WorkflowEventHandle Handle { get; }
}
