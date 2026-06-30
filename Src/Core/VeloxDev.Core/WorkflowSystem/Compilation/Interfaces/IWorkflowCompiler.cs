namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Workflow compiler interface.
/// Traverses the graph topology starting from a given node
/// and produces an ordered list of items.
/// </summary>
public interface IWorkflowCompiler
{
    /// <summary>
    /// Compile the workflow graph into an ordered list of compiled items.
    /// </summary>
    /// <param name="startNode">The entry node to begin traversal from.</param>
    /// <param name="mode">BFS or DFS traversal algorithm.</param>
    /// <param name="direction">Edge traversal direction (Forward / Reverse).</param>
    /// <param name="scope">Traversal scope (FromNode / Omni).</param>
    /// <param name="cycleHandling">How to handle detected cycles.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a cross-domain node is encountered (node.Parent differs from
    /// startNode.Parent), or when CycleHandling.Throw is active and a cycle is found.
    /// </exception>
    CompilationResult Compile(IWorkflowNodeViewModel startNode, CompileMode mode,
        CompileDirection direction = CompileDirection.Forward,
        CompileScope scope = CompileScope.FromNode,
        CycleHandling cycleHandling = CycleHandling.Throw);
}
