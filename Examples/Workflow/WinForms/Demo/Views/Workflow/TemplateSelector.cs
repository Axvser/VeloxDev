// VeloxDev customization: The view selector. The adapter's WorkflowTemplateSelector owns the dispatch and the
// unsupported-item diagnostics; this file says which view each item kind gets.
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
        // Links are drawn by WorkflowLinkLayer, so the pool never asks for a link view here.
        NodeViewFactory = _ => new WorkflowNodeCard();
    }
}
