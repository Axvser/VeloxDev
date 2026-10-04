using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>Runs the benchmarks in this process, against the assembly as built.</summary>
/// <remarks>
/// <para>
/// BenchmarkDotNet's default toolchain builds a fresh project per benchmark and recompiles the whole dependency
/// graph in <c>Release</c>. That path does not work in this repository: <c>VeloxDev.Core</c> references the
/// <c>VeloxDev.Core.Generator</c> package on any configuration other than Debug, version 10.0.0 is not published,
/// and the analyzer that does arrive never runs — so Core fails to compile with hundreds of missing generated
/// members. Release is therefore not buildable repo-wide today, independently of these benchmarks.
/// </para>
/// <para>
/// The workaround is to build with <c>-c Debug -p:Optimize=true</c>: the generator still comes from the project
/// reference (which works), while the JIT sees optimized IL. The in-process toolchain then measures that build
/// directly instead of rebuilding anything. See <c>Src/Verification/README.md</c> for the invocation.
/// </para>
/// </remarks>
public sealed class BenchmarkConfig : ManualConfig
{
    /// <summary>How many iterations each case runs. Reported in the report, so it lives in one place.</summary>
    /// <remarks>
    /// 15 rather than the 6 this used to be. The old value was a time-for-stability compromise, and it was the
    /// wrong place to compromise: BenchmarkDotNet drops the upper outliers before any statistic is computed, so six
    /// iterations leave a median built from as few as five samples — too few for the number to be called a
    /// measurement. Nothing here is free, and this is the cost that buys accuracy rather than the one that only
    /// buys wall-clock.
    /// </remarks>
    internal const int Iterations = 15;

    /// <summary>How many warm-up iterations precede them.</summary>
    /// <remarks>
    /// Three, so tiered JIT has settled one tier further before the measured runs — the warm-up is not part of the
    /// statistic, which is exactly why it is worth spending.
    /// </remarks>
    internal const int Warmups = 3;

    /// <summary>Builds the configuration.</summary>
    public BenchmarkConfig()
    {
        // LaunchCount 必须是 1 —— InProcess 工具链不支持多进程启动。
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(Warmups)
            .WithIterationCount(Iterations)
            .WithLaunchCount(1));
        AddDiagnoser(MemoryDiagnoser.Default);

        // 默认是「工作目录下的 BenchmarkDotNet.Artifacts」—— 从仓库根启动就会落到根上。
        // 改成本工程目录下，与从哪里启动无关。
        WithArtifactsPath(Artifacts.Path);
    }
}
