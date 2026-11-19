namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that reports a failed view operation without tearing down the pool.
/// </summary>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <remarks>
/// The pool reports each failure and continues with the rest of the batch. A diagnostic sink is
/// expected to be cheap and non-throwing; it is called from the materialization path.
/// </remarks>
public interface IViewDiagnostics<TItem>
    where TItem : class
{
    /// <summary>Reports a failed or refused view operation.</summary>
    /// <param name="item">The item being processed, or <see langword="null"/> when the failure is not item-specific.</param>
    /// <param name="stage">The layer that failed.</param>
    /// <param name="exception">The exception, or <see langword="null"/> for a refusal without one.</param>
    void Failed(TItem? item, ViewStage stage, Exception? exception);
}
