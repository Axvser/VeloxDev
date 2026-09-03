namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Compile-time injection: a node implements this interface and receives its compile identity when compilation
/// finishes (global order, in-chain order, this graph's start offset; Order = -1 means the absolute stop state).
/// </summary>
public interface ICompileTimeAware
{
    void AttachCompileTimeContext(ICompileContext context);

    /// <summary>The compile identity injected at compile-time (read-only; Order = -1 means absolute stop). Runtime jumps the execution status code based on it.</summary>
    ICompileContext? CompileContext { get; }
}
