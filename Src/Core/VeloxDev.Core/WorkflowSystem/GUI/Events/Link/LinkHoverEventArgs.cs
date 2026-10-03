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
    public LinkHoverEventArgs(IWorkflowLinkViewModel? link) => Link = link;

    /// <summary>The link now under the pointer, or <see langword="null"/> when the pointer is on none.</summary>
    public IWorkflowLinkViewModel? Link { get; }
}
