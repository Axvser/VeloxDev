using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; Jalium's layout pass provides geometry.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<Panel, FrameworkElement, IWorkflowViewModel>
{
    /// <inheritdoc />
    public void Measure(Panel host, FrameworkElement view, IWorkflowViewModel item)
    {
    }
}
