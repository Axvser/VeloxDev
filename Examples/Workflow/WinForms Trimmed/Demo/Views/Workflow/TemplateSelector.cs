// VeloxDev customization: The view selector. The adapter's WorkflowTemplateSelector owns the dispatch and the
// unsupported-item diagnostics; this file says which view each item kind gets. Rename the view types below if you
// renamed those items, and add SlotViewFactory / TreeViewFactory if your host pools slots or whole trees.
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The view selector: the adapter's <see cref="WorkflowTemplateSelector"/> with this project's node and link views.
/// </summary>
public sealed class TemplateSelector : WorkflowTemplateSelector
{
    public TemplateSelector()
    {
        NodeViewFactory = node => new NodeView { ViewModel = node };
        LinkViewFactory = link => new LinkView();
    }
}
