using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the two ports a connection is being made between, before the link exists.
/// </summary>
/// <remarks>
/// <para>
/// This is the cancellable form of <see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/>: that one is a rule
/// the tree's Helper overrides, while this runs first, so a host can refuse one drag without subclassing its Helper.
/// The two compose — the connection happens only when neither refuses.
/// </para>
/// <para>
/// <c>Connecting</c> carries a handle (<see cref="WorkflowEventHandle.PreventDefault"/> refuses the connection);
/// <c>Connected</c> reports the same pair once the link exists.
/// </para>
/// </remarks>
public sealed class ConnectionEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="sender">The port the connection leaves.</param>
    /// <param name="receiver">The port it arrives at.</param>
    /// <param name="handle">The handle for this action — the same instance the other phase carries.</param>
    public ConnectionEventArgs(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, WorkflowEventHandle handle)
    {
        Sender = sender;
        Receiver = receiver;
        Handle = handle;
    }

    /// <summary>The port the connection leaves.</summary>
    public IWorkflowSlotViewModel Sender { get; }

    /// <summary>The port it arrives at.</summary>
    public IWorkflowSlotViewModel Receiver { get; }

    /// <summary>Veto or silence the connection.</summary>
    public WorkflowEventHandle Handle { get; }
}
