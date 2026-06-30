namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Optional interface for workflow nodes that want to receive execution
/// lifecycle notifications from the Compiler's execution chain.
///
/// If a node's ViewModel implements this interface, <see cref="OnExecutionEvent"/>
/// is called before and after each execution step, allowing the node to update
/// its internal state machine context accordingly.
/// </summary>
public interface ICompileTimeSink
{
    /// <summary>
    /// Called by <see cref="CompilationResult.ExecuteAsync"/> at each stage
    /// of the execution chain. Implement this to react to state transitions.
    /// </summary>
    void OnExecutionEvent(ExecutionContext context);
}
