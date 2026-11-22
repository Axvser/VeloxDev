using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

using VeloxDev.WorkflowSystem.Attachments;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Places a node card through its attachment; links and slots are positioned by their own layers.</summary>
/// <remarks>
/// A node attachment owns the anchor-to-control projection and re-applies it whenever the node or its parent
/// changes. The pool asks once more after the view is in the tree because a recycled view can be rebound while it
/// is already a child, so no parent-change event will run that pass.
/// </remarks>
public sealed class ViewArranger : IViewArranger<IWorkflowComponent, Control>
{
    /// <inheritdoc />
    public void Arrange(Control view, IWorkflowComponent item)
    {
        WorkflowNodeAttachment.For(view)?.ApplyPosition();
    }
}
