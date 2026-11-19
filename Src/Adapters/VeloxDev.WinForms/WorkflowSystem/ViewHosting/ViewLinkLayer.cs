using System.Collections.Generic;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Declines to own links; WinForms materializes one pooled control per link.</summary>
public sealed class ViewLinkLayer : IViewLinkLayer<Control, IWorkflowViewModel>
{
    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public bool Handles(IWorkflowViewModel item) => false;

    /// <inheritdoc />
    public void Render(Control host, IReadOnlyList<IWorkflowViewModel> items)
    {
    }
}
