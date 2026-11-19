using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Returns a constant z-order; Jalium templates bind their own z-order.</summary>
public sealed class ViewLayerPolicy : IViewLayerPolicy<IWorkflowViewModel>
{
    /// <inheritdoc />
    public int GetZOrder(IWorkflowViewModel item) => 0;
}
