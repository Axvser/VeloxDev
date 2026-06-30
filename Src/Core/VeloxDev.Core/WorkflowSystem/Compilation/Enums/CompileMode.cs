namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Workflow graph traversal mode used during compilation.
/// Determines the direction in which nodes are ordered.
/// </summary>
public enum CompileMode
{
    /// <summary>Breadth-first search: process nodes level by level from entry points.</summary>
    BFS,

    /// <summary>Depth-first search: process nodes by following each path to its end first.</summary>
    DFS,
}