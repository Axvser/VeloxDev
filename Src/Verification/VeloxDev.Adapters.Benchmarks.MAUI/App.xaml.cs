namespace VeloxDev.Adapters.Benchmarks.MAUI;

/// <summary>Creates the single benchmark window and hands the parsed run to its page.</summary>
public partial class App : Application
{
    /// <summary>Initializes the MAUI application shell.</summary>
    public App() => InitializeComponent();

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var options = BenchmarkSession.Options;
        var log = BenchmarkSession.Log;
        if (options is null || log is null)
        {
            log?.Write("invalid command line; exiting");
            log?.Dispose();
            Environment.Exit(2);
        }

        log.Write("window: creating benchmark page");
        return new Window(new BenchmarkPage(options, log))
        {
            Title = "VeloxDev view pipeline — MAUI",
            // 页面里那块画布固定为 viewport；窗口再多留一圈给标题栏，别让客户区裁掉画布。
            Width = options.ViewportWidth + 16d,
            Height = options.ViewportHeight + 64d,
        };
    }
}
