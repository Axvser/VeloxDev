using System.Collections.Generic;
using VeloxDev.WorkflowSystem;
using VisualPanel = Easel.Visuals.Controls.Panel;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Declines to own links; the visual backend materializes one pooled view per link.</summary>
public sealed class ViewLinkLayer : IViewLinkLayer<VisualPanel, IWorkflowComponent>
{
    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public bool Handles(IWorkflowComponent item) => false;

    /// <inheritdoc />
    public void Render(VisualPanel host, IReadOnlyList<IWorkflowComponent> items)
    {
    }
}
