using Demo.ViewModels.Workflow;
using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public sealed class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
        => item switch
        {
            NodeViewModel => NodeTemplate ?? throw new InvalidOperationException("NodeTemplate is not set."),
            IWorkflowLinkViewModel => LinkTemplate ?? throw new InvalidOperationException("LinkTemplate is not set."),
            _ => throw new InvalidOperationException($"Unknown workflow item: {item.GetType().Name}")
        };
}
