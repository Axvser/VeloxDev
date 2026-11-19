namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Returns a constant z-order; WinForms pooled views rely on add order and the surface's link arrangement.</summary>
public sealed class ViewLayerPolicy : IViewLayerPolicy<IWorkflowViewModel>
{
    /// <inheritdoc />
    public int GetZOrder(IWorkflowViewModel item) => 0;
}
