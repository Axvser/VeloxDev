using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Immutable snapshot of the current workflow surface state. Passed as the context to the grid-decorator
/// and minimap render fragments of the surface behavior, mirroring the offsets the XAML adapters push into
/// <see cref="IWorkflowGridDecorator"/> and <see cref="IWorkflowMinimapOverlay"/>.
///
/// Promoted to Core from the Razor adapter so the snapshot is a standard data packet across all adapters.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流画布表面状态的不可变快照：滚动偏移 + 视口尺寸 + 内容偏移")]
[AgentContext(AgentLanguages.English, "Immutable snapshot of the workflow surface state: scroll offsets + viewport size + content offsets")]
public sealed record SurfaceViewport(
    IWorkflowTreeViewModel Tree,
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
[AgentContext(AgentLanguages.Chinese, "工作流画布的计算尺寸快照")]
[AgentContext(AgentLanguages.English, "Computed canvas size snapshot of a workflow surface")]
public sealed record SurfaceCanvas(double Width, double Height);
