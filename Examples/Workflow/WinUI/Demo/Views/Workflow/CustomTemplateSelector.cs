using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

public partial class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }
    public DataTemplate? ControllerTemplate { get; set; }
    public DataTemplate? BoolSelectorTemplate { get; set; }
    public DataTemplate? EnumSelectorTemplate { get; set; }
    public DataTemplate? LinkTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return item switch
        {
            ControllerViewModel => ControllerTemplate ??
                throw new InvalidOperationException("ControllerTemplate is not set"),
            BoolSelectorNodeViewModel => BoolSelectorTemplate ??
                throw new InvalidOperationException("BoolSelectorTemplate is not set"),
            EnumSelectorNodeViewModel => EnumSelectorTemplate ??
                throw new InvalidOperationException("EnumSelectorTemplate is not set"),
            NodeViewModel => NodeTemplate ??
                throw new InvalidOperationException("NodeTemplate is not set"),
            IWorkflowLinkViewModel => LinkTemplate ??
                throw new InvalidOperationException("LinkTemplate is not set"),
            _ => throw new InvalidOperationException($"Unknown data type: {item?.GetType().Name}")
        };
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        return SelectTemplateCore(item!);
    }
}
