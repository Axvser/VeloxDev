namespace VeloxDev.WorkflowSystem;

/// <summary>
/// What a subscriber to a routed event uses to change what happens with that one event.
/// </summary>
/// <remarks>
/// <para>
/// One handle travels with one logical action — one instance per action, not one per event — so a subscriber
/// further along the route (on an ancestor helper, or on the <c>…ed</c> report) can read what a subscriber nearer
/// the target decided.
/// </para>
/// <para>
/// The two flags answer two different questions and are independent — and <b>the same two flags are handed out
/// with three kinds of action</b>, so each kind gives them a concrete meaning. The concept is one; the landing
/// points are three. Read the list below as the definition, not as three separate features:
/// </para>
/// <list type="bullet">
/// <item><b>Routed input</b> (pointer, key, wheel). <see cref="PreventDefault"/> keeps the framework's own hand
/// from executing, and what that hand <i>is</i> belongs to the adapter: on a wheel it is the scroll the adapter
/// applies, so preventing one keeps the surface from scrolling and hands the wheel to the subscriber
/// (<see cref="IWorkflowSurfaceScroller"/>); on a pointer or key action the platform's own handling of the input
/// is a separate matter and is not affected — a focus change still happens.
/// <see cref="StopPropagation"/> stops the walk up the ancestor chain (<c>target → its node → tree</c>), so no
/// ancestor helper hears it.</item>
/// <item><b>A model action about to happen</b> — the <c>…ing</c> family (<c>Moving</c>, <c>Resizing</c>,
/// <c>Deleting</c>, <c>ChannelChanging</c>, <c>Connecting</c>). <see cref="PreventDefault"/> means the action is
/// <i>refused</i>: it does not happen at all. The framework asks before it acts, and the subscriber answers.</item>
/// <item><b>The report that it happened</b> — the <c>…ed</c> family. There is nothing left to prevent, so
/// <see cref="PreventDefault"/> has no effect there; <see cref="StopPropagation"/> withholds this one report from
/// the subscribers that would hear it next — how a host stays silent about an action the framework <i>did</i>
/// perform.</item>
/// </list>
/// <para>
/// Setting neither is the default and is exactly what happened before this type existed: nothing obliges a host to
/// touch the handle, and a handle nobody touches changes nothing.
/// </para>
/// <para>
/// A handle is only handed out with routed events. Facts a host reports to the framework — a menu opening, a menu
/// closing — carry none, because there is nothing there to cancel.
/// </para>
/// </remarks>
public sealed class WorkflowEventHandle
{
    /// <summary>
    /// Whether the framework skips its own handling of this action. What that means depends on what kind of action
    /// carries the handle — refused outright on a <c>…ing</c> model action, the framework's own hand stood down on
    /// routed input, no effect on a <c>…ed</c> report. See the remarks on this type.
    /// </summary>
    public bool PreventDefault { get; set; }

    /// <summary>
    /// Whether the event stops travelling here: on routed input no ancestor hears it, on a <c>…ed</c> report the
    /// subscribers after this one do not. The framework's own handling is unaffected.
    /// </summary>
    public bool StopPropagation { get; set; }
}
