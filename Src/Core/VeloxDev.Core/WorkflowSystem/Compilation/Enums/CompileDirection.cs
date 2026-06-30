namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Direction of edge traversal during graph compilation.
/// Controls whether the compiler follows Targets (outputs) or Sources (inputs).
/// Orthogonal to <see cref="CompileScope"/>.
/// </summary>
public enum CompileDirection
{
    /// <summary>
    /// Forward traversal: follow Targets (outputs) to downstream nodes.
    /// </summary>
    Forward,

    /// <summary>
    /// Reverse traversal: follow Sources (inputs) to upstream nodes.
    /// </summary>
    Reverse,
}