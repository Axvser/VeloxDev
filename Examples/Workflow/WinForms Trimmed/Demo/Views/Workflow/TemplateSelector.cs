// VeloxDev customization: The view selector. The adapter's WorkflowTemplateSelector owns the dispatch and the
// unsupported-item diagnostics; this file says which view each item kind gets. Rename the view types below if you
// renamed those items, and add SlotViewFactory / TreeViewFactory if your host pools slots or whole trees.
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The view selector: the adapter's <see cref="WorkflowTemplateSelector"/> with this project's node and link views.
/// </summary>
public sealed class TemplateSelector : WorkflowTemplateSelector
{
    /// <summary>Creates the selector and points each pooled item kind at the view.</summary>
    public TemplateSelector()
    {
        NodeViewFactory = _ => new NodeView();
        LinkViewFactory = link => new LinkView { ViewModel = link };
    }
}
