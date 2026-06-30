namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Workflow graph traversal mode used during compilation.
/// Determines the direction in which nodes are ordered.
/// </summary>
public enum CompileMode
{
    /// <summary>Breadth-first search: process nodes level by level from entry points.</summary>
    BFS,

    /// <summary>
    /// Depth-first search (pre-order): visit the parent node first, then follow each
    /// child path recursively. At the same depth, neighbors are sorted by
    /// <see cref="ICompileTimePriority.CompilePriority"/> (lower = earlier).
    /// For a linear chain n0→n1→n2, Forward+DFS produces [n0, n1, n2].
    /// </summary>
    DFS,
}