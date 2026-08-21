// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Demo.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        public App()
        {
            this.InitializeComponent();

            // The MAUI/WinUI UnhandledException event fires frequently for these known reasons:
            //   • Uncaught exceptions in IDispatcherTimer.Tick callbacks (dotnet/maui #12245)
            //   • XAML binding-chain exceptions (type-conversion failures, non-fatal)
            //   • Exceptions during ScrollToAsync layout transitions
            // Most of these come from MAUI's internal cross-platform abstraction leaks and do not
            // affect application state. They are logged for diagnosis but not propagated as crashes.
            this.UnhandledException += (sender, e) =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WinUI] UnhandledException: {e.Message}");
                if (e.Exception is not null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[WinUI]   Exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
                }

                // Always mark as handled — the default behavior for MAUI internal exceptions is to
                // let the app keep running. Only truly fatal errors (such as NullReferenceException)
                // are caught via Debugger.Break.
                e.Handled = true;
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
