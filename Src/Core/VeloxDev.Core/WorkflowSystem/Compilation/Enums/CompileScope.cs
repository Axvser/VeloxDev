namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Scope of graph traversal during compilation.
/// Controls whether the compiler starts from a single selected node
/// or automatically finds natural start/end points in the entire Tree.
/// Orthogonal to <see cref="CompileDirection"/>.
/// </summary>
public enum CompileScope
{
    /// <summary>
    /// FromNode: start from the given node and traverse outward
    /// following the specified <see cref="CompileDirection"/>.
    /// This is the default behavior — a single explicit starting point.
    /// </summary>
    FromNode,

    /// <summary>
    /// Omni: automatically discover all natural boundary points
    /// in the start node's Tree:
    ///   - When combined with <see cref="CompileDirection.Forward"/>:
    ///     find all entry nodes (in-degree = 0) as start points.
    ///   - When combined with <see cref="CompileDirection.Reverse"/>:
    ///     find all exit nodes (out-degree = 0) as start points.
    /// Multiple start points are supported.
    /// </summary>
    Omni,
}