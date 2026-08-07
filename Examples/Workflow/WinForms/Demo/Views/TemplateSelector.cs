using System;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views;

/// <summary>
/// Assign the four view factories, then use this selector with
/// <c>ViewPool.SetTemplateSelector</c> to materialize workflow item views.
/// </summary>
public sealed class TemplateSelector : IWorkflowTemplateSelector
{
    public Func<IWorkflowNodeViewModel, Control>? NodeViewFactory { get; set; }
    public Func<IWorkflowSlotViewModel, Control>? SlotViewFactory { get; set; }
    public Func<IWorkflowLinkViewModel, Control>? LinkViewFactory { get; set; }
    public Func<IWorkflowTreeViewModel, Control>? TreeViewFactory { get; set; }

    public Control CreateView(object item)
        => item switch
        {
            IWorkflowLinkViewModel link => LinkViewFactory is not null
                ? LinkViewFactory(link)
                : throw new InvalidOperationException("LinkViewFactory is not set."),
            IWorkflowSlotViewModel slot => SlotViewFactory is not null
                ? SlotViewFactory(slot)
                : throw new InvalidOperationException("SlotViewFactory is not set."),
            IWorkflowNodeViewModel node => NodeViewFactory is not null
                ? NodeViewFactory(node)
                : throw new InvalidOperationException("NodeViewFactory is not set."),
            IWorkflowTreeViewModel tree => TreeViewFactory is not null
                ? TreeViewFactory(tree)
                : throw new InvalidOperationException("TreeViewFactory is not set."),
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}")
        };
}
