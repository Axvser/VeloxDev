namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Target that receives the surface's scroll/content offsets for grid + ruler painting.</summary>
public interface IWorkflowGridDecorator
{
    double ScrollOffsetX { get; set; }
    double ScrollOffsetY { get; set; }
    double ContentOffsetX { get; set; }
    double ContentOffsetY { get; set; }
}

/// <summary>Target that receives the viewport + tree for minimap rendering and drag-to-pan.</summary>
public interface IWorkflowMinimapOverlay : IWorkflowGridDecorator
{
    double ViewportWidth { get; set; }
    double ViewportHeight { get; set; }
    IWorkflowTreeViewModel? WorkflowTree { get; set; }
    bool IsMinimapVisible { get; set; }
}
