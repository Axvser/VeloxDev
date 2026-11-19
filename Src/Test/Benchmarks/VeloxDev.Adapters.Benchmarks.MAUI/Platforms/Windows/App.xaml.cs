namespace VeloxDev.Adapters.Benchmarks.MAUI.WinUI;

/// <summary>Provides the WinUI entry point for the MAUI benchmark host.</summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Initializes the WinUI application and suppresses known timer/XAML exception noise.</summary>
    public App()
    {
        InitializeComponent();

        // ViewScheduler 的批次跑在 IDispatcherTimer.Tick 上；WinUI 不会吞那里的异常（dotnet/maui #12245）。
        this.UnhandledException += (sender, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[WinUI] UnhandledException: {e.Message}");
            e.Handled = true;
        };
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
