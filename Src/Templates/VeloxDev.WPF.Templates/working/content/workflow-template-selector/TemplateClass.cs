using System;
using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Assign the four DataTemplate properties in XAML resources, then use this
/// selector with ViewPool.TemplateSelector or another ItemsControl.
/// </summary>
public sealed class TemplateClass : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? SlotTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }

    public DataTemplate? TreeTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
        => item switch
        {
            IWorkflowLinkViewModel => LinkTemplate
                ?? throw new InvalidOperationException("LinkTemplate is not set."),
            IWorkflowSlotViewModel => SlotTemplate
                ?? throw new InvalidOperationException("SlotTemplate is not set."),
            IWorkflowNodeViewModel => NodeTemplate
                ?? throw new InvalidOperationException("NodeTemplate is not set."),
            IWorkflowTreeViewModel => TreeTemplate
                ?? throw new InvalidOperationException("TreeTemplate is not set."),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };
}
