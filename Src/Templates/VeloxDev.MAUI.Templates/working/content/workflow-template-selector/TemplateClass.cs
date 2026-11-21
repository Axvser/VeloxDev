using System;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Assign the four DataTemplate properties in XAML resources, then use this
/// selector with behaviors:ViewPool.TemplateSelector or another items host.
/// </summary>
public sealed class TemplateClass : DataTemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }

    public DataTemplate? SlotTemplate { get; set; }

    public DataTemplate? LinkTemplate { get; set; }

    public DataTemplate? TreeTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item switch
        {
            ILinkComponent => LinkTemplate
                ?? throw new InvalidOperationException("LinkTemplate is not set."),
            ISlotComponent => SlotTemplate
                ?? throw new InvalidOperationException("SlotTemplate is not set."),
            INodeComponent => NodeTemplate
                ?? throw new InvalidOperationException("NodeTemplate is not set."),
            ITreeComponent => TreeTemplate
                ?? throw new InvalidOperationException("TreeTemplate is not set."),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };
}
