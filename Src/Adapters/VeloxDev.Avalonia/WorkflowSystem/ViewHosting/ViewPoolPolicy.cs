namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The Avalonia pool policy: a large batch bounded by one frame, with unlimited recycling.</summary>
public sealed class ViewPoolPolicy : IViewPoolPolicy
{
    /// <summary>Creates a policy with the given batch limit and frame budget.</summary>
    /// <param name="batchSize">Maximum views per batch; values below one are treated as one.</param>
    /// <param name="frameBudgetMilliseconds">Wall-clock budget per batch; zero or negative means no time limit.</param>
    public ViewPoolPolicy(int batchSize = 1024, double frameBudgetMilliseconds = 16d)
    {
        BatchSize = batchSize;
        FrameBudgetMilliseconds = frameBudgetMilliseconds;
    }

    /// <inheritdoc />
    public int BatchSize { get; }

    /// <inheritdoc />
    public double FrameBudgetMilliseconds { get; }

    /// <inheritdoc />
    public int MaxRecycledViewsPerType => 0;
}
