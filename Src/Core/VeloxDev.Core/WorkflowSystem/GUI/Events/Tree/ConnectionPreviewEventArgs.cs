using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the snapshot of a connection drag whenever the pointer enters, leaves or stays over a candidate slot.
/// </summary>
/// <remarks>
/// This is a preview, not a verdict: <see cref="ConnectionPreviewState.InvalidTarget"/> means the framework's
/// rules or a <see cref="WorkflowInput.PreviewValidating"/> subscriber refused this candidate, but the host's
/// <see cref="IWorkflowTreeEvents.Connecting"/> veto is only asked when the pointer is released. A subscriber
/// uses this to paint the rubber band; it does not decide the connection. <see cref="Check"/> carries the reason.
/// </remarks>
public sealed class ConnectionPreviewEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="sender">The port the drag started from, or <see langword="null"/> when the drag has ended.</param>
    /// <param name="receiver">The candidate port under the pointer, or <see langword="null"/>.</param>
    /// <param name="state">The stage the drag is in.</param>
    /// <param name="check">The check for the candidate, or <see langword="null"/> when there is no candidate.</param>
    public ConnectionPreviewEventArgs(
        IWorkflowSlotViewModel? sender, IWorkflowSlotViewModel? receiver, ConnectionPreviewState state,
        ConnectionCheck? check)
    {
        Sender = sender;
        Receiver = receiver;
        State = state;
        Check = check;
    }

    /// <summary>The port the drag started from, or <see langword="null"/> when the drag has ended.</summary>
    public IWorkflowSlotViewModel? Sender { get; }

    /// <summary>The candidate port under the pointer, or <see langword="null"/>.</summary>
    public IWorkflowSlotViewModel? Receiver { get; }

    /// <summary>The stage the drag is in.</summary>
    public ConnectionPreviewState State { get; }

    /// <summary>The check for the candidate, or <see langword="null"/> when there is no candidate.</summary>
    public ConnectionCheck? Check { get; }
}
