using BenchmarkDotNet.Running;
using VeloxDev.Adapters.Benchmarks.Headed;
using VeloxDev.Benchmarks;

namespace VeloxDev.Adapters.Benchmarks;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "headed", StringComparison.OrdinalIgnoreCase))
        {
            HeadedBenchmarkRunner.Run(args[1..]);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BenchmarkArtifactConfig.Create("adapters"));
    }
}
