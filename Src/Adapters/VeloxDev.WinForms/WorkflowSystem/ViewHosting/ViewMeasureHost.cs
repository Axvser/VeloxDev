using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Does not measure explicitly; a WinForms control's size is already its layout result.</summary>
public sealed class ViewMeasureHost : IViewMeasureHost<Control, Control, IWorkflowComponent>
{
    /// <inheritdoc />
    public void Measure(Control host, Control view, IWorkflowComponent item)
    {
    }
}
