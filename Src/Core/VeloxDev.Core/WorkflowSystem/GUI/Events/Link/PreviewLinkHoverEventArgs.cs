using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the link about to become the hovered one, <i>before</i> the framework moves the hover to it.
/// </summary>
/// <remarks>
/// <para>
/// Raised only when the hover would actually change: moving within one link raises nothing, so a subscriber is
/// never asked the same question twice per pointer move.
/// </para>
/// <para>
/// <see cref="WorkflowEventHandle.PreventDefault"/> leaves <see cref="LinkInteraction.HoveredLink"/> and the
/// highlight where they were; <see cref="WorkflowEventHandle.StopPropagation"/> lets the hover move but withholds
/// <see cref="LinkInteraction.HoverChanged"/>. The same handle instance is carried by that Outcome event.
/// </para>
/// </remarks>
/// <seealso cref="LinkInteraction.PreviewHoverChanged"/>
public sealed class PreviewLinkHoverEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link about to be hovered, or <see langword="null"/> when the pointer is leaving every link.</param>
    /// <param name="handle">The handle for this action.</param>
    public PreviewLinkHoverEventArgs(IWorkflowLinkViewModel? link, WorkflowEventHandle handle)
    {
        Link = link;
        Handle = handle;
    }

    /// <summary>The link about to be hovered, or <see langword="null"/> when the hover is being cleared.</summary>
    public IWorkflowLinkViewModel? Link { get; }

    /// <summary>Veto or silence the hover change.</summary>
    public WorkflowEventHandle Handle { get; }
}
