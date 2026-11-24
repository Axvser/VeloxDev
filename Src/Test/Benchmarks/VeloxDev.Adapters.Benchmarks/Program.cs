using BenchmarkDotNet.Running;
using VeloxDev.Adapters.Benchmarks.Headed;
using VeloxDev.Benchmarks;

namespace VeloxDev.Adapters.Benchmarks;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // BDN 的子构建是 Release；这些包版本还没上 NuGet。工作树里的源码替换由
        // Src/Directory.Build.targets 的 VeloxDevLocalCore 开关负责（子进程继承环境变量）。
        Environment.SetEnvironmentVariable("VeloxDevLocalCore", "true");

        if (args.Length > 0 && string.Equals(args[0], "headed", StringComparison.OrdinalIgnoreCase))
        {
            HeadedBenchmarkRunner.Run(args[1..]);
            return;
        }

        // 本工程只能量「工作树 Debug + Optimize」的 Core：BDN 的子构建是 Release，会让适配器去解析
        // NuGet 上尚未发布的 Core/生成器，并在生成器的 pack 步骤与 Easel 的 Release 包上失败。
        // 因此默认走 in-process（调试器的候选装配已在 Debug+Optimize 下构建），调用方不必再传 --inProcess。
        if (!Array.Exists(args, a => a is "-i" or "--inProcess"))
        {
            args = [.. args, "--inProcess"];
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BenchmarkArtifactConfig.Create("adapters"));
    }
}
