using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the link an open menu was about once that link has left the tree. A notification, so there is
/// nothing on it to veto.
/// </summary>
/// <remarks>
/// The menu is the platform's own object and cannot be closed from Core, so this asks rather than does: a host
/// still showing a menu for this link closes it and reports <see cref="ContextMenuPhase.Closed"/> through
/// <see cref="LinkInteraction.Publish(ContextMenuEvent)"/>, which releases the hover as usual. A host whose menu
/// is already down, or was never about this link, does nothing.
/// </remarks>
/// <seealso cref="LinkInteraction.ContextMenuDismissRequested"/>
public sealed class ContextMenuDismissRequestedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link the open menu was about.</param>
    public ContextMenuDismissRequestedEventArgs(IWorkflowLinkViewModel link) => Link = link;

    /// <summary>The link the open menu was about, and which is no longer in the tree.</summary>
    public IWorkflowLinkViewModel Link { get; }
}
