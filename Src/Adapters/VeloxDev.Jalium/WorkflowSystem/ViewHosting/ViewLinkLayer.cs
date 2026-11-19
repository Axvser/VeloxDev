using System.Collections.Generic;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Declines to own links; Jalium materializes one pooled view per link.</summary>
public sealed class ViewLinkLayer : IViewLinkLayer<Panel, IWorkflowViewModel>
{
    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public bool Handles(IWorkflowViewModel item) => false;

    /// <inheritdoc />
    public void Render(Panel host, IReadOnlyList<IWorkflowViewModel> items)
    {
    }
}
