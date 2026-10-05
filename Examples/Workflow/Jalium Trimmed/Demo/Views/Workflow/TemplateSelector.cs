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
