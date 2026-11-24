// VeloxDev customization: Points each kind of workflow item at the DataTemplate that renders it. The tree view's
// resources construct this and hand it to behaviors:ViewPool.TemplateSelector; rename the template keys there if
// you rename them here.
using System;
using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Points each kind of workflow item at the <see cref="DataTemplate"/> that renders it.
/// </summary>
public sealed class TemplateSelector : DataTemplateSelector
{
    /// <summary>The template used for <see cref="INodeComponent"/> items.</summary>
    public DataTemplate? NodeTemplate { get; set; }

    /// <summary>The template used for <see cref="ISlotComponent"/> items.</summary>
    public DataTemplate? SlotTemplate { get; set; }

    /// <summary>The template used for <see cref="ILinkComponent"/> items.</summary>
    public DataTemplate? LinkTemplate { get; set; }

    /// <summary>The template used for <see cref="ITreeComponent"/> items.</summary>

    /// <inheritdoc />
    public override DataTemplate SelectTemplate(object? item, DependencyObject container)
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
}
