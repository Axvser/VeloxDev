
namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Immutable snapshot of the current workflow surface state. Passed as the context to the grid-decorator
/// and minimap render fragments of the surface behavior, mirroring the offsets the XAML adapters push into
/// <see cref="IWorkflowGridDecorator"/> and <see cref="IWorkflowMinimapOverlay"/>.
///
/// Promoted to Core from the Razor adapter so the snapshot is a standard data packet across all adapters.
/// </summary>
public sealed record SurfaceViewport(
    ITreeComponent Tree,
    double ScrollLeft,
    double ScrollTop,
    double ViewportWidth,
    double ViewportHeight,
    double ContentOffsetX,
    double ContentOffsetY);

/// <summary>
/// The computed canvas size of a workflow surface, passed to its content fragment so children
/// (e.g. an SVG link layer) can size themselves to the same canvas.
/// </summary>
public sealed record SurfaceCanvas(double Width, double Height);
