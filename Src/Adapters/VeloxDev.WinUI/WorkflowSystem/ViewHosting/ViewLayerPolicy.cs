using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Returns a constant z-order; WinUI node templates bind their own z-order and the arranger writes <see cref="IWorkflowNodeViewModel.Anchor"/>'s layer.</summary>
public sealed class ViewLayerPolicy : IViewLayerPolicy<IWorkflowViewModel>
{
    /// <inheritdoc />
    public int GetZOrder(IWorkflowViewModel item) => 0;
}
