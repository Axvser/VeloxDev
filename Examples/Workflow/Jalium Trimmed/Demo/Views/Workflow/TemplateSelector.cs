using Jalium.UI;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>Routes workflow items to their views for the node-editor surface's ViewPool
/// (node → NodeView, link → LinkView). Customize per item type here.</summary>
public static class TemplateSelector
{
    /// <summary>Selector the TreeView surface's ViewPool uses.</summary>
    public static IWorkflowTemplateSelector CreateSelector() => new WorkflowViewSelector();

    private sealed class WorkflowViewSelector : IWorkflowTemplateSelector
    {
        public FrameworkElement CreateView(object item) => item switch
        {
            IWorkflowLinkViewModel => new LinkView(),
            IWorkflowNodeViewModel => new NodeView(),
            _ => throw new InvalidOperationException($"No view registered for {item.GetType()}"),
        };
    }
}
