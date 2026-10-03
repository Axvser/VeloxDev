using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the menu a host has just put on screen. A fact, so there is nothing on it to veto.
/// </summary>
/// <remarks>
/// Raised by <see cref="LinkInteraction.Publish(ContextMenuEvent)"/> after the hover has been suspended, so a
/// subscriber that tidies up on "menu is open" sees the surface already in that state.
/// </remarks>
/// <seealso cref="LinkInteraction.ContextMenuOpened"/>
public sealed class ContextMenuOpenedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link the menu is about, or <see langword="null"/> for the canvas' own menu.</param>
    /// <param name="position">Where it opened, in the links' own coordinate space.</param>
    public ContextMenuOpenedEventArgs(IWorkflowLinkViewModel? link, Anchor position)
    {
        Link = link;
        Position = position;
    }

    /// <summary>The link the menu is about, or <see langword="null"/> when it is the canvas' own menu.</summary>
    public IWorkflowLinkViewModel? Link { get; }

    /// <summary>Where it opened.</summary>
    public Anchor Position { get; }
}
