using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Keeps link items out of the main pool; the shared link overlay draws every visible link itself.</summary>
/// <remarks>
/// <see cref="Render"/> is a no-op: <c>WorkflowLinkOverlay</c> already enumerates
/// <see cref="IWorkflowTreeViewModelHelper.VisibleItems"/> and paints the link items from that set. This
/// layer exists so the pool never materializes a link view.
/// </remarks>
public sealed class ViewLinkLayer : IViewLinkLayer<Layout, IWorkflowViewModel>
{
    /// <inheritdoc />
    public bool IsActive => true;

    /// <inheritdoc />
    public bool Handles(IWorkflowViewModel item) => item is IWorkflowLinkViewModel;

    /// <inheritdoc />
    public void Render(Layout host, IReadOnlyList<IWorkflowViewModel> items)
    {
        // 连线由 WorkflowLinkOverlay 从 VisibleItems 直接绘制；本层只负责把它们挡在主池之外。
    }
}
