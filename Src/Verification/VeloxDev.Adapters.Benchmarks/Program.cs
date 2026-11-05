using BenchmarkDotNet.Running;

namespace VeloxDev.Adapters.Benchmarks;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
