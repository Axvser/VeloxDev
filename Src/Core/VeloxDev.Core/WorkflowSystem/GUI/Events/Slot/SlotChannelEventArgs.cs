using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a slot's channel change: what it was and what it is going to be.
/// </summary>
/// <remarks>
/// The channel decides how many connections a slot may hold, and whether it takes senders, receivers or both — so
/// changing it can tear existing connections down. <c>ChannelChanging</c> is where a host refuses that for this one
/// slot, instead of policing every caller.
/// </remarks>
public sealed class SlotChannelEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="slot">The slot being changed.</param>
    /// <param name="from">Its current channel.</param>
    /// <param name="to">The channel it is going to.</param>
    /// <param name="handle">The handle for this action — the same instance the other phase carries.</param>
    public SlotChannelEventArgs(IWorkflowSlotViewModel slot, SlotChannel from, SlotChannel to, WorkflowEventHandle handle)
    {
        Slot = slot;
        From = from;
        To = to;
        Handle = handle;
    }

    /// <summary>The slot being changed.</summary>
    public IWorkflowSlotViewModel Slot { get; }

    /// <summary>Its current channel.</summary>
    public SlotChannel From { get; }

    /// <summary>The channel it is going to.</summary>
    public SlotChannel To { get; }

    /// <summary>Veto or silence the change.</summary>
    public WorkflowEventHandle Handle { get; }
}
