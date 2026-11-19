using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// `--policy fixed` 的中性对照：与 Avalonia 适配器自带的 ViewPoolPolicy 同参同义，
// 让 WPF / WinForms / Jalium 不需要为了基准引用别家的策略类型。
internal sealed class BenchmarkFixedPoolPolicy : IViewPoolPolicy
{
    public BenchmarkFixedPoolPolicy(int batchSize, double frameBudgetMilliseconds)
    {
        BatchSize = batchSize;
        FrameBudgetMilliseconds = frameBudgetMilliseconds;
    }

    public int BatchSize { get; }

    public double FrameBudgetMilliseconds { get; }

    public int MaxRecycledViewsPerType => 0;
}
