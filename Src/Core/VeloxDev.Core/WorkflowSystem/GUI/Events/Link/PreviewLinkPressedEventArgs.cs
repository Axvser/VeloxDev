using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries a link that is about to be pressed, <i>before</i> the framework selects it or reports the press.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WorkflowEventHandle.PreventDefault"/> leaves the hover alone and raises no
/// <see cref="LinkInteraction.LinkPressed"/> — the press is swallowed for this link.
/// <see cref="WorkflowEventHandle.StopPropagation"/> lets the selection happen but withholds the report.
/// </para>
/// <para>
/// The right button reaches here too: the context menu request is decided after the selection, so refusing the
/// press also refuses the menu.
/// </para>
/// </remarks>
/// <seealso cref="LinkInteraction.PreviewLinkPressed"/>
public sealed class PreviewLinkPressedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link about to be pressed.</param>
    /// <param name="button">The button doing it.</param>
    /// <param name="handle">The handle for this action.</param>
    public PreviewLinkPressedEventArgs(IWorkflowLinkViewModel link, PointerButtonKind button, WorkflowEventHandle handle)
    {
        Link = link;
        Button = button;
        Handle = handle;
    }

    /// <summary>The link about to be pressed.</summary>
    public IWorkflowLinkViewModel Link { get; }

    /// <summary>The button doing it.</summary>
    public PointerButtonKind Button { get; }

    /// <summary>Veto or silence the press.</summary>
    public WorkflowEventHandle Handle { get; }
}
