using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Leaves geometry to the visual templates; the pool's arrange step is a no-op.</summary>
public sealed class ViewArranger : IViewArranger<IWorkflowComponent, VisualElement>
{
    /// <inheritdoc />
    public void Arrange(VisualElement view, IWorkflowComponent item)
    {
    }
}
