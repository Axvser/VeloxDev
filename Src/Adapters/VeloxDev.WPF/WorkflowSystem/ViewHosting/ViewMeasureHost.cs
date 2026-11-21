using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; WPF's layout pass provides geometry.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<Panel, FrameworkElement, IWorkflowComponent>
{
    /// <inheritdoc />
    public void Measure(Panel host, FrameworkElement view, IWorkflowComponent item)
    {
    }
}
