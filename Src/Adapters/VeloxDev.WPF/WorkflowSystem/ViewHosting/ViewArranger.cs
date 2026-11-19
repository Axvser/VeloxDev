using System.Windows;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Leaves geometry to the WPF templates; the pool's arrange step is a no-op.</summary>
public sealed class ViewArranger : IViewArranger<IWorkflowViewModel, FrameworkElement>
{
    /// <inheritdoc />
    public void Arrange(FrameworkElement view, IWorkflowViewModel item)
    {
    }
}
