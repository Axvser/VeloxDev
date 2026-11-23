using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VisualPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.Panel;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; the visual surface's layout pass provides geometry.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<VisualPanel, VisualElement, IWorkflowComponent>
{
    /// <inheritdoc />
    public void Measure(VisualPanel host, VisualElement view, IWorkflowComponent item)
    {
    }
}
