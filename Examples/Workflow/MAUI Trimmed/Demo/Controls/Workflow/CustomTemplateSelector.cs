using Demo.ViewModels.Workflow;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public sealed class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }
    public DataTemplate? LinkTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item switch
        {
            NodeViewModel => NodeTemplate ?? throw new InvalidOperationException("NodeTemplate is not set."),
            IWorkflowLinkViewModel => LinkTemplate ?? throw new InvalidOperationException("LinkTemplate is not set."),
            _ => throw new InvalidOperationException($"Unknown workflow item: {item?.GetType().Name}")
        };
}
