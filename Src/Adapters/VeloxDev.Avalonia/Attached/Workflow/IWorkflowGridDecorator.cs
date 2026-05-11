namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Implemented by grid decorator views to receive scroll and content offset updates
/// without requiring reflection-based property assignment.
/// </summary>
public interface IWorkflowGridDecorator
{
    double ScrollOffsetX { get; set; }
    double ScrollOffsetY { get; set; }
    double ContentOffsetX { get; set; }
    double ContentOffsetY { get; set; }
}
