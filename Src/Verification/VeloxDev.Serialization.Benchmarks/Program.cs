using BenchmarkDotNet.Running;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>
/// Runs the serialization benchmarks at every data magnitude and leaves a Markdown report behind.
/// </summary>
/// <remarks>
/// No arguments runs all four magnitudes of both classes and writes
/// <c>BenchmarkDotNet.Artifacts/serialization-performance.md</c>. BenchmarkDotNet's own arguments still work
/// (<c>--filter</c>, <c>--list</c>, …), and the report then covers whatever ran.
/// </remarks>
public static class Program
{
    /// <summary>Entry point.</summary>
    /// <param name="args">BenchmarkDotNet arguments; empty runs every benchmark.</param>
    public static void Main(string[] args)
    {
        // 空参数下**不能**交给 BenchmarkSwitcher：它会进交互式选择，无人应答就什么也不跑、还不报错。
        // 带参数走它（`--filter` 之类的都在那里），不带就把两个类全跑掉。
        var summaries = args.Length == 0
            ? BenchmarkRunner.Run([typeof(SerializationBenchmarks), typeof(ComparisonBenchmarks)])
            : BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

        var path = PerformanceReport.Write(summaries);

        Console.WriteLine();
        Console.WriteLine($"报告已写入：{path}");
    }
}
