using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public sealed class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ControllerTemplate { get; set; }
    public DataTemplate? NodeTemplate { get; set; }
    public DataTemplate? BoolSelectorTemplate { get; set; }
    public DataTemplate? EnumSelectorTemplate { get; set; }
    public DataTemplate? LinkTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item switch
        {
            ControllerViewModel => ControllerTemplate ?? throw new InvalidOperationException("ControllerTemplate is not set."),
            BoolSelectorNodeViewModel => BoolSelectorTemplate ?? throw new InvalidOperationException("BoolSelectorTemplate is not set."),
            EnumSelectorNodeViewModel => EnumSelectorTemplate ?? throw new InvalidOperationException("EnumSelectorTemplate is not set."),
            NodeViewModel => NodeTemplate ?? throw new InvalidOperationException("NodeTemplate is not set."),
            IWorkflowLinkViewModel => LinkTemplate ?? throw new InvalidOperationException("LinkTemplate is not set."),
            _ => throw new InvalidOperationException($"Unknown data type: {item?.GetType().Name}")
        };
}
