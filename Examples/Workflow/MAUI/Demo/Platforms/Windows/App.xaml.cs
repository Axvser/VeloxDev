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
            // 这些异常大部分来自 MAUI 内部的跨平台抽象泄漏，不影响应用状态。
            // 记录它们以便排查，但不传播崩溃。
            this.UnhandledException += (sender, e) =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WinUI] UnhandledException: {e.Message}");
                if (e.Exception is not null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[WinUI]   Exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
                }

                // 始终标记为已处理 — MAUI 内部异常的默认行为是让应用继续运行。
                // 只有真正的致命错误（如 NullReferenceException）通过 Debugger.Break 捕获。
                e.Handled = true;
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
