using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a candidate the framework is about to mark as valid, so a host can refuse it for this preview only.
/// </summary>
/// <remarks>
/// <para>
/// Raised by <see cref="WorkflowInput.PreviewValidating"/> once per new candidate — and again on an explicit
/// <see cref="StandardEx.WorkflowTreeEx.StandardRefreshConnectionPreview"/> — never on every pointer move. Call
/// <see cref="Reject"/> to turn the candidate red without affecting the release-time decision; a candidate the
/// framework already refused is not offered here, because a preview may only tighten the framework's rules,
/// never relax them.
/// </para>
/// <para>
/// The final decision still belongs to <see cref="IWorkflowTreeEvents.Connecting"/> and
/// <see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/> on release. This event is advisory.
/// </para>
/// </remarks>
public sealed class ConnectionPreviewValidatingEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="sender">The port the drag started from.</param>
    /// <param name="receiver">The candidate port under the pointer.</param>
    /// <param name="check">The framework's check for the pair.</param>
    public ConnectionPreviewValidatingEventArgs(
        IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, ConnectionCheck check)
    {
        Sender = sender;
        Receiver = receiver;
        Check = check;
    }

    /// <summary>The port the drag started from.</summary>
    public IWorkflowSlotViewModel Sender { get; }

    /// <summary>The candidate port under the pointer.</summary>
    public IWorkflowSlotViewModel Receiver { get; }

    /// <summary>
    /// The current check. It starts as the framework's verdict; <see cref="Reject"/> replaces it with
    /// <see cref="ConnectionRejectReason.RejectedByHost"/>.
    /// </summary>
    public ConnectionCheck Check { get; private set; }

    /// <summary>Refuses this candidate for the preview.</summary>
    /// <param name="message">An optional explanation a view can display.</param>
    public void Reject(string? message = null)
        => Check = ConnectionCheck.Rejected(ConnectionRejectReason.RejectedByHost, message);
}
