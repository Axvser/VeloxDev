using Demo.ViewModels.Workflow;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public sealed class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
        => item switch
        {
            NodeViewModel => NodeTemplate ?? throw new InvalidOperationException("NodeTemplate is not set."),
            IWorkflowLinkViewModel => LinkTemplate ?? throw new InvalidOperationException("LinkTemplate is not set."),
            _ => throw new InvalidOperationException($"Unknown workflow item: {item.GetType().Name}")
        };

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
