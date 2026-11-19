using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks.WinUI;

// `--policy fixed` 的中性对照：与适配器自带策略同参同义，不依赖别家的策略类型。
internal sealed class WinUiBenchmarkFixedPoolPolicy : IViewPoolPolicy
{
    internal WinUiBenchmarkFixedPoolPolicy(int batchSize, double frameBudgetMilliseconds)
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
