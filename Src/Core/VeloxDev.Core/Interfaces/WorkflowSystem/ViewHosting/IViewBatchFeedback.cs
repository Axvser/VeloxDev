namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Optional closed-loop feedback from the view pool to its policy: one call after each materialization
/// batch, so an adaptive policy can react to the current machine and view cost.
/// </summary>
/// <remarks>
/// The call runs on the host thread, immediately after the batch. Implementations must be cheap and
/// must not allocate per batch.
/// </remarks>
public interface IViewBatchFeedback
{
    /// <summary>Reports a completed batch.</summary>
    /// <param name="processed">The number of views actually materialized in the batch.</param>
    /// <param name="elapsedTicks">Elapsed time in <see cref="System.Diagnostics.Stopwatch"/> ticks.</param>
    /// <param name="pending">The number of items still waiting after this batch.</param>
    void OnBatchCompleted(int processed, long elapsedTicks, int pending);
}
