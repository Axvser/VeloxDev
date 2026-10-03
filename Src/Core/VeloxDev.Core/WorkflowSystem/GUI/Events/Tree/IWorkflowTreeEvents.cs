using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Optional capability of a tree's Helper: it raises the connection events, so a host can refuse one drag without
/// subclassing the Helper to override <see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/>.
/// </summary>
/// <remarks>
/// Kept off <see cref="IWorkflowTreeViewModelHelper"/> for the same reason as <see cref="IWorkflowNodeEvents"/>.
/// </remarks>
public interface IWorkflowTreeEvents
{
    /// <summary>
    /// Raised when a connection between two ports is about to be made — before the tree's own
    /// <c>ValidateConnection</c> rule is asked, so the two compose.
    /// </summary>
    event EventHandler<ConnectionEventArgs>? Connecting;

    /// <summary>Raised once the link exists, carrying the same handle as <see cref="Connecting"/>.</summary>
    event EventHandler<ConnectionEventArgs>? Connected;

    /// <summary>Asks whether the connection may be made; the handle then goes to <see cref="RaiseConnected"/>.</summary>
    /// <param name="sender">The port the connection leaves.</param>
    /// <param name="receiver">The port it arrives at.</param>
    WorkflowEventHandle RaiseConnecting(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver);

    /// <summary>Reports a connection that happened, unless the handle silenced it.</summary>
    /// <param name="sender">The port the connection leaves.</param>
    /// <param name="receiver">The port it arrives at.</param>
    /// <param name="handle">The handle <see cref="RaiseConnecting"/> returned.</param>
    void RaiseConnected(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, WorkflowEventHandle handle);
}
