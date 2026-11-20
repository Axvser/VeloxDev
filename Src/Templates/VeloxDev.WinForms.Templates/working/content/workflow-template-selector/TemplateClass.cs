// VeloxDev customization: which view each pooled item kind gets. Links are not pooled on the default surface
// (WorkflowLinkLayer draws them); LinkViewFactory is used only by a host that opts into per-link views.
// Rename the view types below if you renamed those items; add SlotViewFactory / TreeViewFactory if your host pools
// slots or whole trees.
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The view selector: the adapter's <see cref="WorkflowTemplateSelector"/> with this project's node and link views.
/// </summary>
public sealed class TemplateClass : WorkflowTemplateSelector
{
    /// <summary>Creates the selector and points each pooled item kind at the generated view.</summary>
    public TemplateClass()
    {
        NodeViewFactory = _ => new NodeView();
        LinkViewFactory = link => new LinkView { ViewModel = link };
    }
}
