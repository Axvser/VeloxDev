// VeloxDev customization: which visual view each pooled item kind gets.
using Demo.ViewModels;
using Easel.Visuals;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Chooses the visual view for each item, preferring the tree's DataTemplate resources.</summary>
public sealed class CustomTemplateSelector : VisualTemplateSelector
{
    public DataTemplate? ControllerTemplate { get; set; }

    public DataTemplate? TimerTemplate { get; set; }

    public DataTemplate? PythonTemplate { get; set; }

    public DataTemplate? EnumTemplate { get; set; }

    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? SlotTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }

    /// <inheritdoc />
    public override VisualElement? SelectTemplate(object? item) => item switch
    {
        ControllerViewModel => ControllerTemplate?.Create() ?? new ControllerView(),
        TimerNodeViewModel => TimerTemplate?.Create() ?? new TimerNodeView(),
        PythonScriptNodeViewModel => PythonTemplate?.Create() ?? new PythonNodeView(),
        EnumSelectorNodeViewModel => EnumTemplate?.Create() ?? new EnumSelectorNodeView(),
        INodeComponent => NodeTemplate?.Create() ?? new WorkflowNodeCard(),
        ISlotComponent => SlotTemplate?.Create() ?? new FullSlotView(),
        ILinkComponent => LinkTemplate?.Create() ?? new LinkView(),
        _ => null,
    };
}
