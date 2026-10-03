using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a right press, before anything opens: which link the menu would be about (or none, for the canvas'
/// own menu) and where it landed.
/// </summary>
/// <remarks>
/// <para>
/// The menu itself stays the host's — it has to place a platform popup — so this event is a notification with a
/// veto rather than an instruction. <see cref="WorkflowEventHandle.PreventDefault"/> means "no menu here", which is
/// how a host disables the menu for a link, a kind of link, or a whole surface without unwiring anything.
/// </para>
/// <para>
/// The right press still reaches <see cref="LinkInteraction.LinkPressed"/> afterwards, unchanged: hosts that open
/// their menu from there keep working, and a host that has moved to this event should stop opening from
/// <c>LinkPressed</c> rather than have both fire.
/// </para>
/// </remarks>
/// <seealso cref="LinkInteraction.ContextMenuRequested"/>
public sealed class ContextMenuRequestedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link under the press, or <see langword="null"/> when the press landed on empty canvas.</param>
    /// <param name="position">Where the press landed, in the links' own coordinate space.</param>
    /// <param name="handle">The handle for this request.</param>
    public ContextMenuRequestedEventArgs(IWorkflowLinkViewModel? link, Anchor position, WorkflowEventHandle handle)
    {
        Link = link;
        Position = position;
        Handle = handle;
    }

    /// <summary>The link the menu would be about, or <see langword="null"/> for the canvas' own menu.</summary>
    public IWorkflowLinkViewModel? Link { get; }

    /// <summary>Where the press landed.</summary>
    public Anchor Position { get; }

    /// <summary>Refuse the menu for this press.</summary>
    public WorkflowEventHandle Handle { get; }
}
