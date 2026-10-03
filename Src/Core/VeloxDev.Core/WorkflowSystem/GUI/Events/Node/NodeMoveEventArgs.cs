using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a node placement: where it was and where it is going.
/// </summary>
/// <remarks>
/// <para>
/// Both anchors are complete placements — <see cref="Anchor.Layer"/> included — so a host that applies
/// <see cref="To"/> itself, or keeps <see cref="From"/> to undo later, does not silently drop the node's layer.
/// An anchor built for an event must never be a bare <c>new Anchor(x, y, 0)</c>.
/// </para>
/// <para>
/// Raised twice for one move: <c>Moving</c> before the framework applies it — where
/// <see cref="WorkflowEventHandle.PreventDefault"/> pins the node where it is — and <c>Moved</c> after, carrying
/// the same handle.
/// </para>
/// </remarks>
public sealed class NodeMoveEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="node">The node being placed.</param>
    /// <param name="from">Where it is now, layer included.</param>
    /// <param name="to">Where it is going, layer included.</param>
    /// <param name="handle">The handle for this action — the same instance the other phase carries.</param>
    public NodeMoveEventArgs(IWorkflowNodeViewModel node, Anchor from, Anchor to, WorkflowEventHandle handle)
    {
        Node = node;
        From = from;
        To = to;
        Handle = handle;
    }

    /// <summary>The node being placed.</summary>
    public IWorkflowNodeViewModel Node { get; }

    /// <summary>Where the node is now — a complete placement, layer included.</summary>
    public Anchor From { get; }

    /// <summary>Where the node is going — a complete placement, layer included.</summary>
    public Anchor To { get; }

    /// <summary>Veto or silence the move.</summary>
    public WorkflowEventHandle Handle { get; }
}
