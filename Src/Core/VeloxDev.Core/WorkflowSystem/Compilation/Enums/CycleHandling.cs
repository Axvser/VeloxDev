namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Strategy for handling cycles detected during graph compilation.
/// </summary>
public enum CycleHandling
{
    /// <summary>Throw an exception when a cycle is detected.</summary>
    Throw,

    /// <summary>
    /// Silently trim cycle edges. The BFS/DFS traversal naturally avoids
    /// revisiting nodes via the global visited set, producing a spanning tree.
    /// No cycle metadata is preserved in the compiled result.
    /// </summary>
    Trim,

    /// <summary>
    /// Allow cycles and preserve cycle metadata.
    /// The compiler annotates the loop entry and tail nodes (<see cref="CompiledItem.IsLoopEntry"/>
    /// and <see cref="CompiledItem.LoopTailId"/>) for informational or external use.
    /// The executor runs all items once (each node appears exactly once in the
    /// compiled list), so no runtime loop tracking or circuit breaker is applied.
    /// </summary>
    Allow,
}