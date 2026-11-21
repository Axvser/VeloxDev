using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; MAUI's layout pass provides the view geometry.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<Layout, View, IWorkflowComponent>
{
    /// <inheritdoc />
    public void Measure(Layout host, View view, IWorkflowComponent item)
    {
    }
}
