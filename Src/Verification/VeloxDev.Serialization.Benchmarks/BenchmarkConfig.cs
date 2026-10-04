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
    internal const int Iterations = 6;

    /// <summary>How many warm-up iterations precede them.</summary>
    internal const int Warmups = 2;

    /// <summary>Builds the configuration.</summary>
    public BenchmarkConfig()
    {
        // 迭代次数比 ShortRun 多、但比过去少：10 次那一版一次全量跑要九分钟，而报告自带的自校量出这一档的
        // **噪声本身就有 5–15%**（同一个操作量两次之差），多跑四次并不改变任何结论。6 次是省时与稳定的折中。
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
