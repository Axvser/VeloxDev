// VeloxDev customization: The view selector. The adapter's WorkflowTemplateSelector owns the dispatch and the
// unsupported-item diagnostics; this file says which view each item kind gets. Rename the view types below if you
// renamed those items, and add SlotViewFactory / TreeViewFactory if your host pools slots or whole trees.
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The view selector: the adapter's <see cref="WorkflowTemplateSelector"/> with this project's node and link views.
/// </summary>
public sealed class TemplateClass : WorkflowTemplateSelector
{
    public TemplateClass()
    {
        NodeViewFactory = node => new NodeView { ViewModel = node };
        // 连线视图由适配器那份「附加」收绑（池会找它），所以这里不必传 ViewModel。
        LinkViewFactory = link => new LinkView();
    }
}
