using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Leaves geometry to Jalium's templates; the pool's arrange step is a no-op.</summary>
/// <remarks>
/// Node templates bind <c>Canvas.Left</c>/<c>Canvas.Top</c>, and link views move their own box through
/// <see cref="VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowLinkBounds"/>; writing geometry here would
/// fight both.
/// </remarks>
public sealed class ViewArranger : IViewArranger<IWorkflowViewModel, FrameworkElement>
{
    /// <inheritdoc />
    public void Arrange(FrameworkElement view, IWorkflowViewModel item)
    {
    }
}
