using System.Collections.Generic;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Keeps link items out of the main pool; <see cref="AttachedBehaviors.WorkflowLinkLayer"/> draws them in one pass.</summary>
/// <remarks>
/// WinForms has no lightweight visual: one pooled view per link is one window per link. The linked layer is attached
/// to the surface canvas, so the pool only materializes nodes and the canvas paints every visible link itself.
/// <see cref="Render"/> only invalidates the canvas; the paint pass enumerates the visible links.
/// </remarks>
public sealed class ViewLinkLayer : IViewLinkLayer<Control, IWorkflowViewModel>
{
    /// <inheritdoc />
    public bool IsActive => true;

    /// <inheritdoc />
    public bool Handles(IWorkflowViewModel item) => item is IWorkflowLinkViewModel;

    /// <inheritdoc />
    public void Render(Control host, IReadOnlyList<IWorkflowViewModel> items)
    {
        if (!host.IsDisposed)
        {
            host.Invalidate();
        }
    }
}
