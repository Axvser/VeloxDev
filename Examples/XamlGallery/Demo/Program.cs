using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;

namespace Demo;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // WinForms 的原生暗色在句柄创建时决定；--light / --contrast 让整个进程从一开始就用那套调色板。
        var mode = args.Contains("--contrast", StringComparer.OrdinalIgnoreCase)
            ? ThemeMode.HighContrast
            : args.Contains("--light", StringComparer.OrdinalIgnoreCase)
                ? ThemeMode.Light
                : ThemeMode.Dark;
        Theme.Mode = mode;

        Application.SetColorMode(mode == ThemeMode.Light ? SystemColorMode.Classic : SystemColorMode.Dark);

        using var form = new GalleryForm(args);
        GalleryCapture.ApplyStartup(form, args);
        if (GalleryCapture.TryRun(form, args))
        {
            // 截图模式：窗口显示后逐条抓图，抓完自己关掉。
            Application.Run(form);
            return;
        }

        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            // 冒烟模式：窗口真正显示一次，1.2 秒后自己关掉，运行完不留窗口。
            var timer = new System.Windows.Forms.Timer { Interval = 1200 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                form.Close();
            };
            form.Shown += (_, _) => timer.Start();
            Application.Run(form);
            Environment.ExitCode = 0;
            return;
        }

        Application.Run(form);
    }
}
