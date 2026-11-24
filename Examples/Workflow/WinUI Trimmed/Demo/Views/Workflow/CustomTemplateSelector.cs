using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Assign the three DataTemplate properties in XAML resources, then use this
/// selector with ViewPool.TemplateSelector or another items host.
/// </summary>
public sealed class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? SlotTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }


    protected override DataTemplate SelectTemplateCore(object item)
        => item switch
        {
            ILinkComponent => LinkTemplate
                ?? throw new InvalidOperationException("LinkTemplate is not set."),
            ISlotComponent => SlotTemplate
                ?? throw new InvalidOperationException("SlotTemplate is not set."),
            INodeComponent => NodeTemplate
                ?? throw new InvalidOperationException("NodeTemplate is not set."),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
