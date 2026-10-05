using System;
using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Points each kind of workflow item at the <c>DataTemplate</c> that renders it.
/// </summary>
/// <remarks>
/// Assign the four properties in the tree view's resources and hand the instance to
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
    /// <summary>The template used for <see cref="IWorkflowNodeViewModel"/> items.</summary>
    public DataTemplate? NodeTemplate { get; set; }

    /// <summary>The template used for <see cref="IWorkflowSlotViewModel"/> items.</summary>
    public DataTemplate? SlotTemplate { get; set; }

    /// <summary>The template used for <see cref="IWorkflowLinkViewModel"/> items.</summary>
    public DataTemplate? LinkTemplate { get; set; }

    /// <summary>The template used for <see cref="IWorkflowTreeViewModel"/> items.</summary>
    public DataTemplate? TreeTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate SelectTemplate(object? item, DependencyObject container)
        => item switch
        {
            IWorkflowLinkViewModel => LinkTemplate
                ?? throw new InvalidOperationException("LinkTemplate is not set."),
            IWorkflowSlotViewModel => SlotTemplate
                ?? throw new InvalidOperationException("SlotTemplate is not set."),
            IWorkflowNodeViewModel => NodeTemplate
                ?? throw new InvalidOperationException("NodeTemplate is not set."),
            IWorkflowTreeViewModel => TreeTemplate
                ?? throw new InvalidOperationException("TreeTemplate is not set."),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };
}
