using Avalonia.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Leaves geometry to Avalonia's templates; the pool's arrange step is a no-op.</summary>
public sealed class ViewArranger : IViewArranger<IWorkflowComponent, Control>
{
    /// <inheritdoc />
    public void Arrange(Control view, IWorkflowComponent item)
    {
    }
}
