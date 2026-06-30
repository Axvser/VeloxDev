namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Workflow compiler interface.
/// Traverses the graph topology starting from a given node
/// and produces an ordered list of items.
/// </summary>
public interface IWorkflowCompiler
{
    /// <summary>
    /// Compile the workflow graph into one or more ordered execution plans.
    /// </summary>
    /// <param name="startNode">The entry node to begin traversal from.</param>
    /// <param name="mode">BFS or DFS traversal algorithm.</param>
    /// <param name="direction">Edge traversal direction (Forward / Reverse).</param>
    /// <param name="scope">Traversal scope.
    ///   <c>FromNode</c>: returns a single result rooted at <paramref name="startNode"/>.
    ///   <c>Omni</c>: returns one result per discovered entry node (in-degree=0 for Forward,
    ///   out-degree=0 for Reverse). Each result is an independent subgraph.
    /// </param>
    /// <param name="cycleHandling">How to handle detected cycles.</param>
    /// <returns>
    /// A read-only list of <see cref="CompilationResult"/>. For <c>FromNode</c> the list
    /// contains exactly one element. For <c>Omni</c> the list contains one element per
    /// natural boundary node in the graph.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a cross-domain node is encountered (node.Parent differs from
    /// startNode.Parent), or when CycleHandling.Throw is active and a cycle is found.
    /// </exception>
    IReadOnlyList<CompilationResult> Compile(IWorkflowNodeViewModel startNode, CompileMode mode,
        CompileDirection direction = CompileDirection.Forward,
        CompileScope scope = CompileScope.FromNode,
        CycleHandling cycleHandling = CycleHandling.Throw);
}
