namespace VeloxDev.WorkflowSystem;

/// <summary>
/// What a host implements to receive a slot's model events — currently its channel changes.
/// </summary>
/// <remarks>
/// Same shape as <see cref="IWorkflowNodeEventSink"/>: the argument carries the handle, so an
/// <c>On…ing</c> method can refuse that one action. See <see cref="WorkflowEventRelay"/> for how a platform hooks
/// this up.
/// </remarks>
public interface IWorkflowSlotEventSink
{
    /// <summary>Called before the slot's channel changes; refuse via the argument's handle to keep it.</summary>
    /// <param name="e">The change that is about to happen.</param>
    void OnChannelChanging(SlotChannelEventArgs e);

    /// <summary>Called after the slot's channel changed.</summary>
    /// <param name="e">The change that happened.</param>
    void OnChannelChanged(SlotChannelEventArgs e);
}
