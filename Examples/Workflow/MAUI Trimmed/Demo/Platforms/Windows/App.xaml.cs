// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Demo.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        public App()
        {
            this.InitializeComponent();

            // MAUI/WinUI 的 UnhandledException 事件频繁因以下已知原因触发：
            //   • IDispatcherTimer.Tick 回调中未捕获的异常 (dotnet/maui #12245)
            //   • XAML 绑定链路异常（类型转换失败，非致命）
            //   • ScrollToAsync 布局过渡期异常
            this.UnhandledException += (sender, e) =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WinUI] UnhandledException: {e.Message}");
                if (e.Exception is not null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[WinUI]   Exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
                }
                e.Handled = true;
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
