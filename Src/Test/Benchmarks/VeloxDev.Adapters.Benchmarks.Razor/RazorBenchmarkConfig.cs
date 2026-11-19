using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace VeloxDev.Adapters.Benchmarks.Razor;

/// <summary>Runs the Razor render benchmarks in this process, against the assemblies as built.</summary>
/// <remarks>
/// <para>
/// The default toolchain builds a fresh project in <c>Release</c>, which this repository cannot do: the Razor adapter
/// and Core take their generator from a package outside <c>Debug</c>. The in-process toolchain measures the build the
/// caller produced instead. Build with <c>-c Debug -p:Optimize=true</c>, as <c>Src/Test/README.md</c> does for
/// the other archived benchmarks.
/// </para>
/// <para>
/// Every invocation is one measurement: <c>InvocationCount</c> is 1 and <c>UnrollFactor</c> is 1. That is what lets
/// the initial-render case own a fresh <see cref="Microsoft.AspNetCore.Components.Web.HtmlRenderer"/> per iteration
/// instead of measuring an update, and what keeps the scroll case at one full sweep per iteration.
/// </para>
/// <para>
/// Build the graph with <c>-p:Optimize=true</c> before running. BenchmarkDotNet's optimizer validator is left on:
/// if Core or the Razor adapter was built unoptimized, the run is refused instead of silently reporting inflated
/// times.
/// </para>
/// </remarks>
public sealed class RazorBenchmarkConfig : ManualConfig
{
    /// <summary>Builds the configuration shared by every benchmark in this assembly.</summary>
    public RazorBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithLaunchCount(1)
            .WithWarmupCount(2)
            .WithIterationCount(10)
            .WithInvocationCount(1)
            .WithUnrollFactor(1));
        AddDiagnoser(MemoryDiagnoser.Default);
        WithArtifactsPath(Artifacts.Path);
    }
}
