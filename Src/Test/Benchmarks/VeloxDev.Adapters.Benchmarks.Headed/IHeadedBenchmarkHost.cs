namespace VeloxDev.Adapters.Benchmarks.Headed;

/// <summary>The per-adapter seam: one host owns one real window and one pipeline instance.</summary>
/// <remarks>
/// Implementations materialize views through their adapter's own scheduler and only return after the
/// pool's pending batches have drained and the host has completed one render/layout turn.
/// </remarks>
public interface IHeadedBenchmarkHost : IDisposable
{
    /// <summary>Rebuilds the synthetic workflow and the adapter pipeline from an empty viewport.</summary>
    /// <returns>A task that completes when the reset has settled.</returns>
    Task ResetScenarioAsync();

    /// <summary>Moves the viewport to the content origin and measures the initial materialization.</summary>
    /// <returns>One measured step.</returns>
    Task<PipelineSnapshot> MeasureInitialAsync();

    /// <summary>Runs the diagonal viewport sweep and measures every step.</summary>
    /// <returns>One snapshot per scroll step.</returns>
    Task<IReadOnlyList<PipelineSnapshot>> MeasureScrollAsync();

    /// <summary>Sets the viewport to the full content bounds and measures the worst-case materialization.</summary>
    /// <returns>One measured step.</returns>
    Task<PipelineSnapshot> MeasureFullAsync();
}

/// <summary>One measured step: time, allocations, and the pipeline's own counters.</summary>
/// <param name="ElapsedTicks">Wall-clock ticks spent in the change plus quiescence.</param>
/// <param name="AllocatedBytes">Managed allocations during the step.</param>
/// <param name="Gen0">Gen0 collections during the step.</param>
/// <param name="Gen1">Gen1 collections during the step.</param>
/// <param name="Gen2">Gen2 collections during the step.</param>
/// <param name="ActiveViews">Views the pool currently holds as active.</param>
/// <param name="PendingViews">Items waiting for a materialization batch.</param>
/// <param name="RecycledViews">Views currently parked for reuse.</param>
/// <param name="CreatedViews">Cumulative views the factory constructed.</param>
/// <param name="ReusedViews">Cumulative views dequeued from the recycle pool.</param>
/// <param name="DestroyedViews">Cumulative views removed instead of recycled.</param>
/// <param name="VisibleItems">Size of the visible set at snapshot time.</param>
public readonly record struct PipelineSnapshot(
    long ElapsedTicks,
    long AllocatedBytes,
    int Gen0,
    int Gen1,
    int Gen2,
    int ActiveViews,
    int PendingViews,
    int RecycledViews,
    int CreatedViews,
    int ReusedViews,
    int DestroyedViews,
    int VisibleItems);
