namespace VeloxDev.WorkflowSystem;

/// <summary>
/// What a host implements to receive a node's model events. Hand an instance to the platform's attached property
/// (or subscribe to the Helper directly where the platform has no markup), and the framework calls these methods as
/// the node is placed, resized or torn down.
/// </summary>
/// <remarks>
/// <para>
/// Every method receives the same argument object the Helper's own event carries, so the
/// <see cref="WorkflowEventHandle"/> travels with it: set <see cref="WorkflowEventHandle.PreventDefault"/> in an
/// <c>On…ing</c> method to refuse that one action, exactly as a direct subscriber would.
/// </para>
/// <para>
/// The methods are called synchronously, before (for <c>OnMoving</c>/<c>OnResizing</c>/<c>OnDeleting</c>) or after
/// (for the rest) the framework touches the model — a sink that blocks blocks the interaction.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowEventRelay"/>
public interface IWorkflowNodeEventSink
{
    /// <summary>Called before the node is placed somewhere new; refuse via the argument's handle to pin it.</summary>
    /// <param name="e">The placement, both anchors complete (layer included).</param>
    void OnMoving(NodeMoveEventArgs e);

    /// <summary>Called after the node was placed.</summary>
    /// <param name="e">The placement that happened.</param>
    void OnMoved(NodeMoveEventArgs e);

    /// <summary>Called before the node's size changes; refuse via the argument's handle to keep it.</summary>
    /// <param name="e">The resize that is about to happen.</param>
    void OnResizing(NodeResizeEventArgs e);

    /// <summary>Called after the node's size changed.</summary>
    /// <param name="e">The resize that happened.</param>
    void OnResized(NodeResizeEventArgs e);

    /// <summary>Called before the node is torn down; refuse via the argument's handle to keep it in the tree.</summary>
    /// <param name="e">The node about to be deleted.</param>
    void OnDeleting(NodeEventArgs e);

    /// <summary>Called after the node was torn down.</summary>
    /// <param name="e">The node that was deleted.</param>
    void OnDeleted(NodeEventArgs e);
}
