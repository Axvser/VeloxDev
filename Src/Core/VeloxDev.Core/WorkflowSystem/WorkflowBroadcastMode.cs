namespace VeloxDev.Core.WorkflowSystem;

/// <summary>
/// Defines the traversal strategy used by workflow broadcast and reverse-broadcast operations.
/// <para><see cref="Parallel"/> ( default )</para>
/// <para><see cref="BreadthFirst"/></para>
/// <para><see cref="DepthFirst"/> </para>
/// </summary>
public enum WorkflowBroadcastMode
{
    /// <summary>
    /// Propagates to all reachable adjacent nodes immediately, preserving the original parallel behavior.
    /// </summary>
    Parallel = 0,

    /// <summary>
    /// Propagates level by level using a queue, which is suitable for <c>BFS</c>-style execution.
    /// </summary>
    BreadthFirst = 1,

    /// <summary>
    /// Propagates branch by branch using a stack, which is suitable for <c>DFS</c>-style execution.
    /// </summary>
    DepthFirst = 2,
}
