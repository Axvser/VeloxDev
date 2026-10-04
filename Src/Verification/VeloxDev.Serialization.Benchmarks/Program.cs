using BenchmarkDotNet.Running;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>Runs the serialization benchmarks. Pass BenchmarkDotNet's usual arguments to filter or export.</summary>
public static class Program
{
    /// <summary>Entry point.</summary>
    /// <param name="args">BenchmarkDotNet arguments; empty runs every benchmark.</param>
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
