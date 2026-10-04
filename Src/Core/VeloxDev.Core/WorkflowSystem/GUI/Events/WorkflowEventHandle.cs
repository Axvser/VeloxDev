namespace VeloxDev.WorkflowSystem;

/// <summary>
/// What a subscriber to a <b>Preview</b> event uses to change what the framework does with that one event.
/// </summary>
/// <remarks>
/// <para>
/// A logical action is raised twice: once as a <b>Preview</b> event <i>before</i> the framework's own handling, and
/// once as an <b>Outcome</b> event <i>after</i> it. The handle travels with both — one instance per action, not one
/// per event — so an Outcome subscriber can read what a Preview subscriber decided.
/// </para>
/// <para>
/// The two flags answer two different questions and are independent:
/// </para>
/// <list type="bullet">
/// <item><see cref="PreventDefault"/> — do not do the framework's own thing this time, and report no Outcome.
/// It is how a subscriber nearer the target refuses one action without changing anything for the whole surface:
/// the handler that would have acted reads it and stands down.</item>
/// <item><see cref="StopPropagation"/> — do the framework's own thing, but report no Outcome event. Rare, but
/// coherent: it is how a host hides an action from other subscribers while keeping the behaviour.</item>
/// </list>
/// <para>
/// Setting neither is the default and is exactly what happened before this type existed: nothing obliges a host to
/// touch the handle, and a handle nobody touches changes nothing.
/// </para>
/// <para>
/// A handle is only handed out with Preview and Outcome events. Facts a host reports to the framework — a menu
/// opening, a menu closing — carry none, because there is nothing there to cancel.
/// </para>
/// </remarks>
public sealed class WorkflowEventHandle
{
    /// <summary>
    /// Whether the framework should skip its own handling of this action. Left <see langword="true"/> the action
    /// does not happen at all, and no Outcome event is raised for it.
    /// </summary>
    public bool PreventDefault { get; set; }

    /// <summary>
    /// Whether the Outcome event should be withheld from other subscribers. The framework's own handling still
    /// runs; only the report is suppressed.
    /// </summary>
    public bool StopPropagation { get; set; }

    /// <summary>Whether <see cref="PreventDefault"/> was set — readable from the Outcome event's handle.</summary>
    public bool IsDefaultPrevented => PreventDefault;

    /// <summary>Whether <see cref="StopPropagation"/> was set — readable from the Outcome event's handle.</summary>
    public bool IsPropagationStopped => StopPropagation;
}
