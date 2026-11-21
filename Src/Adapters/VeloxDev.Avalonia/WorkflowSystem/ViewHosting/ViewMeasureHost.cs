using Avalonia.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; Avalonia's layout pass provides geometry.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<Panel, Control, IWorkflowComponent>
{
    /// <inheritdoc />
    public void Measure(Panel host, Control view, IWorkflowComponent item)
    {
    }
}
