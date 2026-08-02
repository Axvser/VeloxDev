using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Immutable snapshot of the current workflow surface state. Passed as the context to the
/// <c>GridDecorator</c> and <c>Minimap</c> render fragments of <see cref="WorkflowSurfaceBehavior"/>,
/// mirroring the offsets the XAML adapters push into <see cref="IWorkflowGridDecorator"/> and
/// <see cref="IWorkflowMinimapOverlay"/>.
/// </summary>
public sealed record SurfaceViewport(
    IWorkflowTreeViewModel Tree,
    double ScrollLeft,
    double ScrollTop,
    double ViewportWidth,
    double ViewportHeight,
    double ContentOffsetX,
    double ContentOffsetY);

/// <summary>
/// The computed canvas size of a <see cref="WorkflowSurfaceBehavior"/>, passed to its
/// <c>ChildContent</c> fragment so children (e.g. an SVG link layer) can size themselves
/// to the same canvas.
/// </summary>
public sealed record SurfaceCanvas(double Width, double Height);
