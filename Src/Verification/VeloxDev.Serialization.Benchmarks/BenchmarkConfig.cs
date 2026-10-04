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
    /// <summary>Builds the configuration.</summary>
    public BenchmarkConfig()
    {
        // 迭代次数比 ShortRun 多：进程内容易受 JIT 与 GC 进度影响，3 次迭代量出来的误差能到均值的 100%。
        // LaunchCount 必须是 1 —— InProcess 工具链不支持多进程启动。
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithLaunchCount(1));
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
