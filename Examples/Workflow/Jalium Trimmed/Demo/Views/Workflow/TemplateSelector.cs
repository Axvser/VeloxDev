// VeloxDev customization: The view selector. The adapter's WorkflowTemplateSelector owns the dispatch and the
// unsupported-item diagnostics; this file says which view each item kind gets. Rename the view types below if you
// renamed those items, and add SlotViewFactory / TreeViewFactory if your host pools slots or whole trees.
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The view selector the node-editor surface's view pool uses.
/// </summary>
public static class TemplateSelector
{
    /// <summary>Creates a selector wired to this project's node and link views.</summary>
    /// <returns>The selector.</returns>
    public static IWorkflowTemplateSelector CreateSelector() => new Selector();

    private sealed class Selector : WorkflowTemplateSelector
    {
        public Selector()
        {
            NodeViewFactory = _ => new NodeView();
            LinkViewFactory = _ => new LinkView();
        }
    }
}
