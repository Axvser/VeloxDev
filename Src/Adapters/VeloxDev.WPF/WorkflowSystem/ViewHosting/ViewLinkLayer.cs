using System.Collections.Generic;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Declines to own links; WPF materializes one pooled view per link.</summary>
public sealed class ViewLinkLayer : IViewLinkLayer<Panel, IWorkflowComponent>
{
    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public bool Handles(IWorkflowComponent item) => false;

    /// <inheritdoc />
    public void Render(Panel host, IReadOnlyList<IWorkflowComponent> items)
    {
    }
}
