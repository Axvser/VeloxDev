using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the hovered link that the Delete key is about to remove, <i>before</i> the delete is requested or
/// performed.
/// </summary>
/// <remarks>
/// <para>
/// This is where a host gates one deletion: <see cref="WorkflowEventHandle.PreventDefault"/> refuses it for this
/// link only — the per-event answer that used to require turning <see cref="LinkInteraction.AutoDelete"/> off for
/// the whole surface and re-implementing deletion for every link.
/// </para>
/// <para>
/// <see cref="WorkflowEventHandle.StopPropagation"/> still lets the framework delete, but withholds
/// <see cref="LinkInteraction.LinkDeleteRequested"/>. The two flags are independent; the usual "confirm first"
/// shape is <c>PreventDefault</c> plus a later <c>DeleteCommand.Execute(null)</c> of the host's own.
/// </para>
/// </remarks>
/// <seealso cref="LinkInteraction.PreviewLinkDeleteRequested"/>
public sealed class PreviewLinkDeleteRequestedEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The hovered link the Delete key is aimed at.</param>
    /// <param name="handle">The handle for this action.</param>
    public PreviewLinkDeleteRequestedEventArgs(IWorkflowLinkViewModel link, WorkflowEventHandle handle)
    {
        Link = link;
        Handle = handle;
    }

    /// <summary>The link about to be deleted.</summary>
    public IWorkflowLinkViewModel Link { get; }

    /// <summary>Veto or silence the deletion.</summary>
    public WorkflowEventHandle Handle { get; }
}
