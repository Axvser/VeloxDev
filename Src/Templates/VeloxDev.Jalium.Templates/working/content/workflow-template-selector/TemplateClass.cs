using System;
using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Points each kind of workflow item at the <c>DataTemplate</c> that renders it.
/// </summary>
/// <remarks>
/// Assign the three properties in the tree view's resources and hand the instance to
/// <c>behaviors:ViewPool.TemplateSelector</c>:
/// <code>
/// &lt;local:TemplateClass x:Key="WorkflowTemplateSelector"
///                        NodeTemplate="{StaticResource NodeTemplate}"
///                        LinkTemplate="{StaticResource LinkTemplate}" /&gt;
/// </code>
/// This selector is asked before the framework's own lookup, so naming a template here is enough to win.
/// </remarks>
public sealed class TemplateClass : DataTemplateSelector
{
    /// <summary>The template used for <see cref="INodeComponent"/> items.</summary>
    public DataTemplate? NodeTemplate { get; set; }

    /// <summary>The template used for <see cref="ISlotComponent"/> items.</summary>
    public DataTemplate? SlotTemplate { get; set; }

    /// <summary>The template used for <see cref="ILinkComponent"/> items.</summary>
    public DataTemplate? LinkTemplate { get; set; }

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
