using Easel.Visuals;
using VeloxDev.WorkflowSystem;
using VisualPanel = Easel.Visuals.Controls.Panel;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; the visual surface's layout pass provides geometry.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<VisualPanel, VisualElement, IWorkflowComponent>
{
    /// <inheritdoc />
    public void Measure(VisualPanel host, VisualElement view, IWorkflowComponent item)
    {
    }
}
