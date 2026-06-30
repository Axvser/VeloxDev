namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Event type describing the current stage of a compilation-chain execution.
/// </summary>
public enum ExecutionEvent
{
    /// <summary>About to execute the node.</summary>
    BeforeExecute,

    /// <summary>Node executed successfully.</summary>
    AfterExecute,

    /// <summary>Node execution failed.</summary>
    OnError,

    /// <summary>All nodes in the chain have finished.</summary>
    OnCompleted,
}