namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Implemented by minimap overlay views to receive scroll, content offset,
/// viewport, and tree updates without requiring reflection-based property assignment.
/// Symmetric to <see cref="IWorkflowGridDecorator"/>.
/// </summary>
public interface IWorkflowMinimapOverlay
{
    double ScrollOffsetX { get; set; }
    double ScrollOffsetY { get; set; }
    double ContentOffsetX { get; set; }
    double ContentOffsetY { get; set; }
    double ViewportWidth { get; set; }
    double ViewportHeight { get; set; }
    IWorkflowTreeViewModel? WorkflowTree { get; set; }
    bool IsMinimapVisible { get; set; }
}
