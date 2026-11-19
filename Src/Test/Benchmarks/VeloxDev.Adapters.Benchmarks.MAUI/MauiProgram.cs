namespace VeloxDev.Adapters.Benchmarks.MAUI;

// 命令行在应用构建前解析：窗口尺寸要按 viewport 定，跑的场景也要在 CreateWindow 前就绪。
internal static class MauiProgram
{
    internal static MauiApp CreateMauiApp()
    {
        BenchmarkSession.Initialize(Environment.GetCommandLineArgs().Skip(1).ToArray());

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        return builder.Build();
    }
}
