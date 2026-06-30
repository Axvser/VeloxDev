namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Strategy for handling cycles detected during graph compilation.
/// </summary>
public enum CycleHandling
{
    /// <summary>Throw an exception when a cycle is detected.</summary>
    Throw,

    /// <summary>
    /// Carefully trim cycle edges so that all nodes remain in the execution order.
    /// Kahn's algorithm produces a partial order; remaining (cyclic) nodes
    /// are appended in a stable order so no logic chain is lost.
    /// </summary>
    Trim,

    /// <summary>
    /// Allow cycles. The compiler extracts the loop structure (entry + tail)
    /// so the executor can track consecutive iterations and apply a circuit breaker.
    /// </summary>
    Allow,
}