using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks.MAUI;

// `--policy fixed` 的中性对照：Core 的 IViewPoolPolicy 最小实现，与其余宿主同参同义。
internal sealed class FixedViewPoolPolicy : IViewPoolPolicy
{
    public FixedViewPoolPolicy(int batchSize, double frameBudgetMilliseconds)
    {
        BatchSize = batchSize;
        FrameBudgetMilliseconds = frameBudgetMilliseconds;
    }

    public int BatchSize { get; }

    public double FrameBudgetMilliseconds { get; }

    public int MaxRecycledViewsPerType => 0;
}
