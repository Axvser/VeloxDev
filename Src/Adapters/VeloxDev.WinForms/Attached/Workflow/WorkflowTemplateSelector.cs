using System;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Builds a view per workflow item, by declared type.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IWorkflowTemplateSelector"/> — the pool's only creation path on WinForms — by dispatching
/// on the item's type to four factories. Derive from it to assign the ones you render; the dispatch, the
/// unsupported-item diagnostics and the interface plumbing are the same in every host.
/// </para>
/// <para>
/// Leaving a factory unset is an error only for the item kinds that actually reach the pool: it throws on the first
/// item of that kind, naming the factory to assign.
/// </para>
/// </remarks>
public class WorkflowTemplateSelector : IWorkflowTemplateSelector
{
    /// <summary>Builds the card for a node.</summary>
    public Func<IWorkflowNodeViewModel, Control>? NodeViewFactory { get; set; }

    /// <summary>Builds the glyph for a slot.</summary>
    public Func<IWorkflowSlotViewModel, Control>? SlotViewFactory { get; set; }

    /// <summary>Builds the view for a link.</summary>
    public Func<IWorkflowLinkViewModel, Control>? LinkViewFactory { get; set; }

    /// <summary>Builds the view for a whole tree.</summary>
    public Func<IWorkflowTreeViewModel, Control>? TreeViewFactory { get; set; }

    /// <inheritdoc />
    public virtual Control CreateView(object item)
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
            _ => throw new InvalidOperationException($"Unsupported workflow item: {item?.GetType().FullName}"),
        };
}
