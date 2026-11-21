namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Optional compile-time notification: when compilation finishes, the compiler hands the node a copy of its compile
/// identity (global order, in-chain index, entry offset; Order = -1 means the absolute stop state).
/// </summary>
/// <remarks>
/// <para>
/// The engine never reads <see cref="CompileContext"/> back. The authoritative identity lives in the compiled
/// graph (<see cref="CompiledGraph.TryGetCompileInfo"/>), so a node that does not implement this interface still
/// participates fully in join aggregation, redirect skipping and execution status codes — it simply receives no
/// notification. Implement it when the node wants to show its identity (for example a compile badge).
/// </para>
/// <para>
/// The copy is a snapshot: mutating it has no effect on the run, and an exception thrown from
/// <see cref="AttachCompileTimeContext"/> does not fail compilation.
/// </para>
/// </remarks>
public interface ICompileTimeAware
{
    /// <summary>Receives a copy of this node's compile identity; called once per compilation that reaches the node.</summary>
    /// <param name="context">The identity snapshot.</param>
    void AttachCompileTimeContext(ICompileContext context);

    /// <summary>The compile identity received last, or <see langword="null"/> when the node has not been compiled.</summary>
    ICompileContext? CompileContext { get; }
}
