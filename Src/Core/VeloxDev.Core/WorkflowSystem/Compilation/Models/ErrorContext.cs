namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Information about a node failure, passed as the parameter to the error redirect target.
/// The error handler node can inspect this to decide how to recover.
/// </summary>
public sealed class ErrorContext(int failedItemId, Exception exception,
    object? inputParameter, object? lastSuccessfulResult)
{
    /// <summary>The ID of the CompiledItem that failed.</summary>
    public int FailedItemId { get; } = failedItemId;

    /// <summary>The exception that caused the failure.</summary>
    public Exception Exception { get; } = exception;

    /// <summary>The parameter that was being passed when the failure occurred.</summary>
    public object? InputParameter { get; } = inputParameter;

    /// <summary>The result from the last successfully executed node before this failure.</summary>
    public object? LastSuccessfulResult { get; } = lastSuccessfulResult;
}
