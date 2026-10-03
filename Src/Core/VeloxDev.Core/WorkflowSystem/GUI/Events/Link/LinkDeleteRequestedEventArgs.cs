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
    public LinkDeleteRequestedEventArgs(IWorkflowLinkViewModel link) => Link = link;

    /// <summary>The link to delete.</summary>
    public IWorkflowLinkViewModel Link { get; }
}
