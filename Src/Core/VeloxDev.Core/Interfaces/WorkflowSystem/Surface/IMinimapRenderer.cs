namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that draws one minimap pass. The pipeline computes placement and mapped geometry
/// and calls <see cref="Render"/> once per pass; the implementation only translates
/// <see cref="MinimapDrawContext"/> into platform primitives.
/// </summary>
/// <typeparam name="TSurface">The platform drawing surface, such as a WPF <c>DrawingContext</c>.</typeparam>
public interface IMinimapRenderer<TSurface>
{
    /// <summary>
    /// Draws the minimap frame, every node thumbnail in <see cref="MinimapDrawContext.MappedNodes"/>,
    /// and the viewport indicator in <see cref="MinimapDrawContext.MappedViewport"/>.
    /// </summary>
    /// <param name="surface">The platform drawing surface for this pass.</param>
    /// <param name="context">The geometry for this pass, in overlay-local and minimap-local coordinates.</param>
    void Render(TSurface surface, MinimapDrawContext context);
}
