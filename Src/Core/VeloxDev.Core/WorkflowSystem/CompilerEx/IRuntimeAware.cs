namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Runtime injection: a node implements this interface and the compiled-execution engine hands it the current run's
/// <see cref="IRuntimeContext"/> via a method entry before driving it, letting the node record sequence numbers,
/// write logs, and read/write shared variables.
/// </summary>
public interface IRuntimeAware
{
    void AttachRuntimeContext(IRuntimeContext context);
}
