using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Declines to own links; WinUI materializes one pooled view per link.</summary>
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
