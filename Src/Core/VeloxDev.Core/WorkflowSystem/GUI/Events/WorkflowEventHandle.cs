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
/// The two flags answer two different questions and are independent:
/// </para>
/// <list type="bullet">
/// <item><see cref="PreventDefault"/> — the framework's own hand does not execute this time: the handler that
/// would have acted reads it and stands down. It is how a subscriber nearer the target refuses one action without
/// changing anything for the whole surface. What the framework's own hand <i>is</i> belongs to the adapter: on a
/// wheel it is the scroll the adapter applies, so preventing one keeps the surface from scrolling and hands the
/// wheel to the subscriber (<see cref="IWorkflowSurfaceScroller"/>); on a pointer or key action the platform's own
/// handling of the input is a separate matter and is not affected — a focus change still happens.</item>
/// <item><see cref="StopPropagation"/> — the event does not travel further. On the input route that means the
/// walk up the ancestor chain stops (<c>target → its node → tree</c>), so no ancestor helper hears it; on a model
/// action it withholds the <c>…ed</c> report from other subscribers while the framework still does its thing.</item>
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
    /// Whether the framework should skip its own handling of this action. Left <see langword="true"/> that hand
    /// does not execute; the platform's own handling of the input is a separate matter and is not affected.
    /// </summary>
    public bool PreventDefault { get; set; }

    /// <summary>
    /// Whether the event stops travelling here: on the input route no ancestor hears it, on a model action the
    /// <c>…ed</c> report is withheld. The framework's own handling is unaffected.
    /// </summary>
    public bool StopPropagation { get; set; }

    /// <summary>Whether <see cref="PreventDefault"/> was set — readable by whoever gets this handle next.</summary>
    public bool IsDefaultPrevented => PreventDefault;

    /// <summary>Whether <see cref="StopPropagation"/> was set — readable by whoever gets this handle next.</summary>
    public bool IsPropagationStopped => StopPropagation;
}
