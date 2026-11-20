using Microsoft.UI.Xaml;
using VeloxDev.Adapters.Benchmarks.Headed;
using VeloxDev.Benchmarks;

namespace VeloxDev.Adapters.Benchmarks.WinUI;

/// <summary>Boots the unpackaged WinUI host and runs one headed view-pipeline benchmark on the UI thread.</summary>
/// <remarks>
/// The command line is read from <see cref="Environment.GetCommandLineArgs"/>; the parsed options are the
/// same ones the console hosted adapters accept. The report is written through the shared
/// <see cref="HeadedReport"/> rules, so it lands under the OS temp area.
/// </remarks>
public partial class App : Application
{
    private WinUiHeadedHost? _host;
    private Window? _window;

    /// <summary>Creates the application singleton, attaches the parent console and installs the crash sink.</summary>
    public App()
    {
        ConsoleBridge.Attach();

        // WinUI 的启动失败（stowed exception）会直接终结进程，不经过 OnLaunched；先挂上兜底出口。
        UnhandledException += (_, e) => CrashLog.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Report(e.ExceptionObject as Exception);

        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        HeadedOptions options;
        try
        {
            options = ParseCommandLine();
        }
        catch (Exception exception)
        {
            CrashLog.ReportMessage(exception.Message);
            Environment.ExitCode = 2;
            Exit();
            return;
        }

        try
        {
            _host = new WinUiHeadedHost(options);
            _window = _host.Window;
            _host.Show();
            _ = RunAsync(options, _host);
        }
        catch (Exception exception)
        {
            CrashLog.Report(exception);
            Environment.ExitCode = 1;
            Exit();
        }
    }

    private async Task RunAsync(HeadedOptions options, WinUiHeadedHost host)
    {
        var exitCode = 0;
        try
        {
            // 必须在这个 UI 线程上 await：宿主的每次调用（建系统、改视口、排空批）都落在 UI 线程。
            await HeadedBenchmarkCore.ExecuteAsync(options, host);
        }
        catch (Exception exception)
        {
            CrashLog.Report(exception);
            exitCode = 1;
        }
        finally
        {
            host.Dispose();
            _host = null;
            Environment.ExitCode = exitCode;
            Exit();
        }
    }

    // WinUI 自己生成的 Main 不声明 adapter；没有 --adapter 时默认 winui，便于从产物目录直接启动。
    private static HeadedOptions ParseCommandLine()
    {
        var args = Environment.GetCommandLineArgs()[1..];
        var options = HeadedOptions.Parse(args);
        if (!Array.Exists(args, static arg => string.Equals(arg, "--adapter", StringComparison.Ordinal)))
        {
            options.Adapter = "winui";
        }

        return options;
    }
}

// WinExe 崩溃时控制台可能已经关闭；除了 stderr 与 Debug 输出，再留一份文件给事后排查。
internal static class CrashLog
{
    private static readonly string Path = System.IO.Path.Combine(BenchmarkArtifacts.For("winui"), "winui-host.log");

    internal static void Report(Exception? exception) => ReportText(exception?.ToString() ?? "Unknown failure.");

    internal static void ReportMessage(string message) => ReportText(message);

    private static void ReportText(string message)
    {
        // 三条出口彼此独立：没有控制台时 stderr 会抛，文件系统只读时日志会抛，任一条都不该再引发一次崩溃。
        try
        {
            Console.Error.WriteLine(message);
        }
        catch (IOException)
        {
        }

        System.Diagnostics.Debug.WriteLine(message);
        try
        {
            System.IO.Directory.CreateDirectory(BenchmarkArtifacts.For("winui"));
            File.AppendAllText(Path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

// WinExe 没有自己的控制台；挂到启动它的终端，报告才会出现在命令行里而不是消失。
internal static class ConsoleBridge
{
    private const int AttachParentProcess = -1;

    internal static void Attach()
    {
        try
        {
            if (!AttachConsole(AttachParentProcess))
            {
                return;
            }

            // 重定向到文件/管道时句柄已存在，AttachConsole 不会替换它；这里只包装当前标准输出。
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (IOException)
        {
            // 没有可挂接的控制台时保持 WinExe 的静默行为，报告仍写文件。
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
