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
        : this(link, button, new WorkflowEventHandle())
    {
    }

    /// <summary>Creates the argument, carrying the handle the Preview subscriber saw.</summary>
    /// <param name="link">The link that was pressed.</param>
    /// <param name="button">The button that pressed it.</param>
    /// <param name="handle">The handle of the action this reports — the same instance the Preview event carried.</param>
    public LinkPressedEventArgs(IWorkflowLinkViewModel link, PointerButtonKind button, WorkflowEventHandle handle)
    {
        Link = link;
        Button = button;
        Handle = handle;
    }

    /// <summary>The link that was pressed.</summary>
    public IWorkflowLinkViewModel Link { get; }

    /// <summary>The button that pressed it.</summary>
    public PointerButtonKind Button { get; }

    /// <summary>
    /// The handle of the action this reports. Read <see cref="WorkflowEventHandle.IsDefaultPrevented"/> /
    /// <see cref="WorkflowEventHandle.IsPropagationStopped"/> to see what a Preview subscriber decided; writing it
    /// here is a no-op, the framework has already handled the action.
    /// </summary>
    public WorkflowEventHandle Handle { get; }
}
