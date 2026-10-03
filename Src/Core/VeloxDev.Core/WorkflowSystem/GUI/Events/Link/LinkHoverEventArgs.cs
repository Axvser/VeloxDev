using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Carries the link the pointer is now on — or <see langword="null"/> when it left every link.
/// </summary>
/// <remarks>
/// The <c>sender</c> of the event is the visual that drew the link when the platform has one per link
/// (a control under the pointer); on the immediate-mode platforms, where one surface draws them all, it is
/// <see langword="null"/> and this argument is the only way to the link.
/// </remarks>
public sealed class LinkHoverEventArgs : EventArgs
{
    /// <summary>Creates the argument.</summary>
    /// <param name="link">The link now under the pointer, or <see langword="null"/> for none.</param>
    public LinkHoverEventArgs(IWorkflowLinkViewModel? link)
        : this(link, new WorkflowEventHandle())
    {
    }

    /// <summary>Creates the argument, carrying the handle the Preview subscriber saw.</summary>
    /// <param name="link">The link now under the pointer, or <see langword="null"/> for none.</param>
    /// <param name="handle">The handle of the action this reports — the same instance the Preview event carried.</param>
    public LinkHoverEventArgs(IWorkflowLinkViewModel? link, WorkflowEventHandle handle)
    {
        Link = link;
        Handle = handle;
    }

    /// <summary>The link now under the pointer, or <see langword="null"/> when the pointer is on none.</summary>
    public IWorkflowLinkViewModel? Link { get; }

    /// <summary>
    /// The handle of the action this reports. Read <see cref="WorkflowEventHandle.IsDefaultPrevented"/> /
    /// <see cref="WorkflowEventHandle.IsPropagationStopped"/> to see what a Preview subscriber decided; writing it
    /// here is a no-op, the framework has already handled the action.
    /// </summary>
    public WorkflowEventHandle Handle { get; }
}
