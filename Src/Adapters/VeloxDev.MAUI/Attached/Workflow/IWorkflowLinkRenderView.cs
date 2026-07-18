namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Implemented by link render views (e.g., PolylineCurveView) that need
/// to receive canvas offset updates for correct coordinate math.
/// Without this offset, links draw at world coordinates while the canvas
/// shifts via TranslationX, causing visual offset = partial clipping.
/// </summary>
public interface IWorkflowLinkRenderView
{
    double ContentOffsetX { get; set; }
    double ContentOffsetY { get; set; }
}
