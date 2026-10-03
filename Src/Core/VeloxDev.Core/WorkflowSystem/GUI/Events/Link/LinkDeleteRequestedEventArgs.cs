using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a link that the Delete key was pressed on, while it was hovered.
/// </summary>
/// <remarks>
/// <para>
/// This is a request, not a fait accompli: deleting is a policy a host may want to gate — a confirmation, a
/// transaction, an undo group — so the host performs the deletion. The one-liner for a host that has no such
/// policy is <c>link.DeleteCommand.Execute(null)</c>.
/// </para>
/// <para>
/// The <c>sender</c> of the event is the visual that drew the link when the platform has one per link;
/// <see langword="null"/> on the immediate-mode platforms.
/// </para>
/// </remarks>
public sealed class LinkDeleteRequestedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link to delete.</param>
    public LinkDeleteRequestedEventArgs(IWorkflowLinkViewModel link)
        : this(link, new WorkflowEventHandle())
    {
    }

    /// <summary>Creates the argument, carrying the handle the Preview subscriber saw.</summary>
    /// <param name="link">The link to delete.</param>
    /// <param name="handle">The handle of the action this reports — the same instance the Preview event carried.</param>
    public LinkDeleteRequestedEventArgs(IWorkflowLinkViewModel link, WorkflowEventHandle handle)
    {
        Link = link;
        Handle = handle;
    }

    /// <summary>The link to delete.</summary>
    public IWorkflowLinkViewModel Link { get; }

    /// <summary>
    /// The handle of the action this reports. Read <see cref="WorkflowEventHandle.IsDefaultPrevented"/> /
    /// <see cref="WorkflowEventHandle.IsPropagationStopped"/> to see what a Preview subscriber decided; writing it
    /// here is a no-op, the decision was already taken.
    /// </summary>
    public WorkflowEventHandle Handle { get; }
}
