using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a link that was pressed, and the button that did it. A link is only reported when the pointer
/// was actually on it — a press on empty canvas raises nothing.
/// </summary>
/// <remarks>
/// The <c>sender</c> of the event is the visual that drew the link when the platform has one per link;
/// <see langword="null"/> on the immediate-mode platforms.
/// </remarks>
public sealed class LinkPressedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link that was pressed.</param>
    /// <param name="button">The button that pressed it — <see cref="PointerButtonKind.Right"/> opens the menu.</param>
    public LinkPressedEventArgs(IWorkflowLinkViewModel link, PointerButtonKind button)
    {
        Link = link;
        Button = button;
    }

    /// <summary>The link that was pressed.</summary>
    public IWorkflowLinkViewModel Link { get; }

    /// <summary>The button that pressed it.</summary>
    public PointerButtonKind Button { get; }
}
