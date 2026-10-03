using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Optional capability of a node's Helper: it raises the placement and lifetime events, and lets a host refuse them
/// before the framework acts.
/// </summary>
/// <remarks>
/// The capability is not a member of <see cref="IWorkflowNodeViewModelHelper"/>: adding members there would break
/// every existing implementer, and a helper that does not want events should not have to answer for them. A helper
/// that derives from <c>NodeHelper&lt;T&gt;</c> gets this for free.
/// </remarks>
/// <seealso cref="WorkflowEventHandle"/>
public interface IWorkflowNodeEvents
{
    /// <summary>
    /// Raised before the node is placed somewhere new. Refusing here (see <see cref="WorkflowEventHandle"/>) holds
    /// the node where it is.
    /// </summary>
    /// <remarks>The anchors are complete placements — <see cref="Anchor.Layer"/> included.</remarks>
    event EventHandler<NodeMoveEventArgs>? Moving;

    /// <summary>Raised after the node was placed, carrying the same handle as <see cref="Moving"/>.</summary>
    event EventHandler<NodeMoveEventArgs>? Moved;

    /// <summary>Raised before the node's size changes.</summary>
    event EventHandler<NodeResizeEventArgs>? Resizing;

    /// <summary>Raised after the node's size changed, carrying the same handle as <see cref="Resizing"/>.</summary>
    event EventHandler<NodeResizeEventArgs>? Resized;

    /// <summary>Raised before the node is torn down. Refusing here keeps it in the tree.</summary>
    event EventHandler<NodeEventArgs>? Deleting;

    /// <summary>Raised after the node was torn down, carrying the same handle as <see cref="Deleting"/>.</summary>
    event EventHandler<NodeEventArgs>? Deleted;

    /// <summary>Asks whether the node may be placed at <paramref name="to"/>; the handle then goes to <see cref="RaiseMoved"/>.</summary>
    /// <param name="from">Where the node is now, layer included.</param>
    /// <param name="to">Where it is going, layer included.</param>
    WorkflowEventHandle RaiseMoving(Anchor from, Anchor to);

    /// <summary>Reports a placement that happened, unless the handle silenced it.</summary>
    /// <param name="from">Where the node was.</param>
    /// <param name="to">Where it is now.</param>
    /// <param name="handle">The handle <see cref="RaiseMoving"/> returned.</param>
    void RaiseMoved(Anchor from, Anchor to, WorkflowEventHandle handle);

    /// <summary>Asks whether the node may be resized; the handle then goes to <see cref="RaiseResized"/>.</summary>
    /// <param name="from">The current size.</param>
    /// <param name="to">The size it is going to.</param>
    WorkflowEventHandle RaiseResizing(Size from, Size to);

    /// <summary>Reports a resize that happened, unless the handle silenced it.</summary>
    /// <param name="from">The size it was.</param>
    /// <param name="to">The size it is now.</param>
    /// <param name="handle">The handle <see cref="RaiseResizing"/> returned.</param>
    void RaiseResized(Size from, Size to, WorkflowEventHandle handle);

    /// <summary>Asks whether the node may be torn down; the handle then goes to <see cref="RaiseDeleted"/>.</summary>
    WorkflowEventHandle RaiseDeleting();

    /// <summary>Reports a deletion that happened, unless the handle silenced it.</summary>
    /// <param name="handle">The handle <see cref="RaiseDeleting"/> returned.</param>
    void RaiseDeleted(WorkflowEventHandle handle);
}
