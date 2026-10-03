using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the menu that has just gone away. A fact, so there is nothing on it to veto.
/// </summary>
/// <remarks>
/// Raised by <see cref="LinkInteraction.Publish(ContextMenuEvent)"/> after the hover has been released, so the
/// surface is live again by the time this arrives.
/// </remarks>
/// <seealso cref="LinkInteraction.ContextMenuClosed"/>
public sealed class ContextMenuClosedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link the menu was about, or <see langword="null"/> when it was the canvas' own menu.</param>
    public ContextMenuClosedEventArgs(IWorkflowLinkViewModel? link) => Link = link;

    /// <summary>The link the menu was about, or <see langword="null"/> when it was the canvas' own menu.</summary>
    public IWorkflowLinkViewModel? Link { get; }
}
