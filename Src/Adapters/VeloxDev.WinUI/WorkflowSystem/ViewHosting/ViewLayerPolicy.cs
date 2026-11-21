using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Returns a constant z-order; WinUI node templates bind their own z-order and the arranger writes <see cref="INodeComponent.Anchor"/>'s layer.</summary>
public sealed class ViewLayerPolicy : IViewLayerPolicy<IWorkflowComponent>
{
    /// <inheritdoc />
    public int GetZOrder(IWorkflowComponent item) => 0;
}
