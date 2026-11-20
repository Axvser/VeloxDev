using VeloxDev.Benchmarks;

namespace VeloxDev.Adapters.Benchmarks.Razor;

// 产物落在 OS 临时目录，不落在仓库：BenchmarkDotNet 默认相对工作目录写，
// 从根启动会散到根上，从工程启动会散到源码里。
internal static class Artifacts
{
    internal static string Path { get; } = BenchmarkArtifacts.For("adapters-razor");
}
