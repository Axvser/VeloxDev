using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; WinUI's layout pass provides geometry.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<Panel, FrameworkElement, IWorkflowComponent>
{
    /// <inheritdoc />
    public void Measure(Panel host, FrameworkElement view, IWorkflowComponent item)
    {
    }
}
