namespace VeloxDev.Adapters.Benchmarks.Razor;

// 产物落在工程目录而不是启动目录：BenchmarkDotNet 默认相对工作目录写，
// 从仓库根启动就会散到根上。.gitignore 的 BenchmarkDotNet.Artifacts/ 任意深度都匹配。
internal static class Artifacts
{
    internal static string Path { get; } = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "BenchmarkDotNet.Artifacts"));
}
