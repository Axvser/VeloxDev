using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;

namespace Demo.Views;

public partial class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }  // 对应 NodeViewModel
    public DataTemplate? ControllerTemplate { get; set; }  // 对应 ControllerViewModel

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return item switch
        {
            ControllerViewModel => ControllerTemplate ??
                throw new InvalidOperationException("ControllerTemplate is not set"),
            NodeViewModel => NodeTemplate ??
                throw new InvalidOperationException("NodeTemplate is not set"),
            _ => throw new InvalidOperationException($"Unknown data type: {item?.GetType().Name}")
        };
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        Debug.WriteLine($"SelectTemplateCore with container called: {item?.GetType().Name}, Container={container?.GetType().Name}");
        return SelectTemplateCore(item);
    }
}