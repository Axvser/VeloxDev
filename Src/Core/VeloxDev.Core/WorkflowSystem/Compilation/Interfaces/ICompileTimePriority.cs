namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Optional interface for nodes that need deterministic ordering
/// when multiple nodes share the same traversal depth.
///
/// During compilation, the compiler sorts same-depth neighbors
/// by <see cref="CompilePriority"/> (ascending — lower value = earlier).
///
/// Nodes that do not implement this interface default to priority 0.
/// </summary>
public interface ICompileTimePriority
{
    /// <summary>
    /// Priority for compile-time ordering. Lower values are processed first.
    /// Default is 0 when not set. Negative values are allowed for "before default"
    /// ordering; positive values for "after default" ordering.
    /// </summary>
    int CompilePriority { get; set; }
}
