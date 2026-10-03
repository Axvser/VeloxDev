namespace VeloxDev.WorkflowSystem;

/// <summary>
/// What a host implements to receive the tree's model events — the connections made between two of its ports.
/// </summary>
/// <remarks>
/// Same shape as <see cref="IWorkflowNodeEventSink"/>: the argument carries the handle, so
/// <see cref="OnConnecting"/> can refuse that one drag. It composes with
/// <see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/> — the connection happens only when neither
/// refuses. See <see cref="WorkflowEventRelay"/> for how a platform hooks this up.
/// </remarks>
public interface IWorkflowTreeEventSink
{
    /// <summary>Called before a connection is made; refuse via the argument's handle to cancel that drag.</summary>
    /// <param name="e">The two ports the connection would join.</param>
    void OnConnecting(ConnectionEventArgs e);

    /// <summary>Called once the link exists.</summary>
    /// <param name="e">The two ports the connection joined.</param>
    void OnConnected(ConnectionEventArgs e);
}
