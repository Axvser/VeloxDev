using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// Dispatches a workflow item to one of the four item templates (node, slot, link, tree)
/// based on its view-model type, mirroring the WPF template's DataTemplateSelector.
/// Wire the templates and provide an items source, then use it anywhere a collection of
/// workflow view-models needs to be rendered.
/// </summary>
public partial class CustomTemplateSelector : ComponentBase
{
    /// <summary>Gets or sets the workflow items to render (nodes, slots, links, trees).</summary>
    [Parameter]
    public System.Collections.IEnumerable? Items { get; set; }

    /// <summary>Gets or sets a stable key selector for <see cref="ViewPool"/> re-renders.</summary>
    [Parameter]
    public Func<object, object>? KeySelector { get; set; }

    /// <summary>Gets or sets the template for <see cref="IWorkflowNodeViewModel"/> items.</summary>
    [Parameter]
    public RenderFragment<IWorkflowNodeViewModel>? NodeTemplate { get; set; }

    /// <summary>Gets or sets the template for <see cref="IWorkflowSlotViewModel"/> items.</summary>
    [Parameter]
    public RenderFragment<IWorkflowSlotViewModel>? SlotTemplate { get; set; }

    /// <summary>Gets or sets the template for <see cref="IWorkflowLinkViewModel"/> items.</summary>
    [Parameter]
    public RenderFragment<IWorkflowLinkViewModel>? LinkTemplate { get; set; }

    /// <summary>Gets or sets the template for <see cref="IWorkflowTreeViewModel"/> items.</summary>
    [Parameter]
    public RenderFragment<IWorkflowTreeViewModel>? TreeTemplate { get; set; }

    private RenderFragment? Select(object item)
        => item switch
        {
            IWorkflowLinkViewModel link => LinkTemplate is null
                ? throw new InvalidOperationException("LinkTemplate is not set.")
                : LinkTemplate(link),
            IWorkflowSlotViewModel slot => SlotTemplate is null
                ? throw new InvalidOperationException("SlotTemplate is not set.")
                : SlotTemplate(slot),
            IWorkflowNodeViewModel node => NodeTemplate is null
                ? throw new InvalidOperationException("NodeTemplate is not set.")
                : NodeTemplate(node),
            IWorkflowTreeViewModel tree => TreeTemplate is null
                ? throw new InvalidOperationException("TreeTemplate is not set.")
                : TreeTemplate(tree),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };
}
