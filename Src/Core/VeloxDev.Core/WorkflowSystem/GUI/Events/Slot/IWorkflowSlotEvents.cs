using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Optional capability of a slot's Helper: it raises the channel events, so a host can refuse one change before the
/// framework tears connections down.
/// </summary>
/// <remarks>
/// Kept off <see cref="IWorkflowSlotViewModelHelper"/> for the same reason as <see cref="IWorkflowNodeEvents"/>:
/// growing that interface would break every implementer, and events are opt-in.
/// </remarks>
public interface IWorkflowSlotEvents
{
    /// <summary>Raised before the slot's channel changes. Refusing here protects this slot from the connections a
    /// channel change can tear down.</summary>
    event EventHandler<SlotChannelEventArgs>? ChannelChanging;

    /// <summary>Raised after the channel changed, carrying the same handle as <see cref="ChannelChanging"/>.</summary>
    event EventHandler<SlotChannelEventArgs>? ChannelChanged;

    /// <summary>Asks whether the channel may change; the handle then goes to <see cref="RaiseChannelChanged"/>.</summary>
    /// <param name="from">The current channel.</param>
    /// <param name="to">The channel it is going to.</param>
    WorkflowEventHandle RaiseChannelChanging(SlotChannel from, SlotChannel to);

    /// <summary>Reports a channel change that happened, unless the handle silenced it.</summary>
    /// <param name="from">The channel it was.</param>
    /// <param name="to">The channel it is now.</param>
    /// <param name="handle">The handle <see cref="RaiseChannelChanging"/> returned.</param>
    void RaiseChannelChanged(SlotChannel from, SlotChannel to, WorkflowEventHandle handle);
}
