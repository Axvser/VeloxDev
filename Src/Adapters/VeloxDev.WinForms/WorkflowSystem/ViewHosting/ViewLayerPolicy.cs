using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Returns a constant z-order; the visual templates bind their own z-order.</summary>
public sealed class ViewLayerPolicy : IViewLayerPolicy<IWorkflowComponent>
{
    /// <inheritdoc />
    public int GetZOrder(IWorkflowComponent item) => 0;
}
