using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public sealed class CustomTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ControllerTemplate { get; set; }
    public DataTemplate? NodeTemplate { get; set; }
    public DataTemplate? BoolSelectorTemplate { get; set; }
    public DataTemplate? EnumSelectorTemplate { get; set; }
    public DataTemplate? PythonTemplate { get; set; }
    public DataTemplate? TimerTemplate { get; set; }
    public DataTemplate? LinkTemplate { get; set; }
    public DataTemplate? VirtualLinkTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item switch
        {
            ControllerViewModel => ControllerTemplate ?? throw new InvalidOperationException("ControllerTemplate is not set."),
            BoolSelectorNodeViewModel => BoolSelectorTemplate ?? throw new InvalidOperationException("BoolSelectorTemplate is not set."),
            EnumSelectorNodeViewModel => EnumSelectorTemplate ?? throw new InvalidOperationException("EnumSelectorTemplate is not set."),
            // New FULL-demo nodes without a dedicated view here → fall back to the generic node card.
            TimerNodeViewModel => TimerTemplate ?? throw new InvalidOperationException("TimerTemplate is not set."),
            LogicGateNodeViewModel => NodeTemplate ?? throw new InvalidOperationException("NodeTemplate is not set."),
            PythonScriptNodeViewModel => PythonTemplate ?? throw new InvalidOperationException("PythonTemplate is not set."),
            NodeViewModel => NodeTemplate ?? throw new InvalidOperationException("NodeTemplate is not set."),
            // VirtualLink's slots have no parent node — use that to distinguish from regular links
            IWorkflowLinkViewModel link when link.Sender.Parent is null || link.Receiver.Parent is null
                => VirtualLinkTemplate ?? throw new InvalidOperationException("VirtualLinkTemplate is not set."),
            IWorkflowLinkViewModel => LinkTemplate ?? throw new InvalidOperationException("LinkTemplate is not set."),
            _ => throw new InvalidOperationException($"Unknown data type: {item?.GetType().Name}")
        };
}
