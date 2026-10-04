namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Optional capability of a component's Helper: it carries the pointer and keyboard events that were routed to that
/// component.
/// </summary>
/// <remarks>
/// <para>
/// Subscribe on <see cref="Input"/> to hear the standard input for one component — the route reaches the target
/// first and then bubbles up its ancestors, and every element on the way sees the same argument instance.
/// </para>
/// <para>
/// The capability is not a member of <c>IWorkflow…ViewModelHelper</c>: adding members there would break every
/// existing implementer, and a helper that does not want input should not have to answer for it. A helper that
/// derives from <c>NodeHelper&lt;T&gt;</c> and its siblings gets this for free.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowInput"/>
public interface IWorkflowInputEvents
{
    /// <summary>The events this component receives while an input event routed through it.</summary>
    WorkflowInputRelay Input { get; }
}
