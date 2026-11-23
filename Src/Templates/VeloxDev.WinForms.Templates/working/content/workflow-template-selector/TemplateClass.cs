// VeloxDev customization: which visual each item kind gets.
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

public sealed class TemplateClass : VisualTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? SlotTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }

    public override VisualElement? SelectTemplate(object? item) => item switch
    {
        INodeComponent => NodeTemplate?.Create() ?? new NodeView(),
        ISlotComponent => SlotTemplate?.Create() ?? new SlotView(),
        ILinkComponent => LinkTemplate?.Create() ?? new LinkView(),
        _ => null,
    };
}
