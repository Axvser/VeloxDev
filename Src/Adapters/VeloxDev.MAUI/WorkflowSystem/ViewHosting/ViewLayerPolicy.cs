using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Returns a constant z-order; MAUI templates and XAML child order decide which layer draws on top.</summary>
public sealed class ViewLayerPolicy : IViewLayerPolicy<IWorkflowViewModel>
{
    /// <inheritdoc />
    public int GetZOrder(IWorkflowViewModel item) => 0;
}
