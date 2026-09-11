using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VeloxDev.AT.Engine;

/// <summary>
/// Owns the lifetime of one demo process: start it, remember it, and make sure it dies.
/// </summary>
/// <remarks>
/// A demo is started from its built executable by absolute path rather than referenced as a project. A
/// <c>ProjectReference</c> would drag MAUI and WinUI into a project that only ever talks to them over UI Automation,
/// and would cost the acceptance project its single target framework and its freedom from workloads.
/// </remarks>
internal sealed class DesktopProcessHost : IDisposable
{
    // 进程表是作业对象之外的第二道防线：作业分配可能因为宿主本身已在某个不可突围的作业里而失败。
    private static readonly ConcurrentDictionary<int, string> Launched = new();

    private Process? _process;
    private JobObject? _job;

    /// <summary>
    /// Whether the demo is actually inside the kill-on-close job. Exposed because the job is the only teardown that
    /// survives the test host being killed outright, and an assignment that quietly did nothing would leave that
    /// guarantee looking intact while it was gone.
    /// </summary>
    internal bool IsInKillOnCloseJob { get; private set; }

    /// <summary>Whether the demo process is still alive.</summary>
    internal bool IsRunning
    {
        get
        {
            try
            {
                return _process is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>The demo's process id, which is what an automation client attaches to.</summary>
    internal int ProcessId => _process?.Id
        ?? throw new InvalidOperationException("The demo has not been started yet.");

    /// <summary>The demo's window title, for failure messages.</summary>
    internal string WindowTitle
    {
        get
        {
            try
            {
                return _process?.MainWindowTitle ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Start the demo from an absolute path and put it under a kill-on-close job.</summary>
    /// <exception cref="FileNotFoundException">The demo has not been built where the driver expects it.</exception>
    internal void Start(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"No demo executable at '{executablePath}'. Build the demo first, or set VELOXDEV_AT_DEMO_ROOT to the directory its output lives under.",
                executablePath);
        }

        _job ??= JobObject.CreateKillOnClose();

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
        };

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{executablePath}'.");

        Launched[_process.Id] = Path.GetFileName(executablePath);
        IsInKillOnCloseJob = _job.Assign(_process);
    }

    /// <summary>
    /// Write a PNG of the demo's window. Returns the path written, or <c>null</c> when the window could not be
    /// captured — a screenshot is a diagnostic, and failing to take one must never be the reason a test fails.
    /// </summary>
    internal string? CaptureScreenshot(string path)
    {
        var handle = _process?.MainWindowHandle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return null;

        var (width, height) = (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        if (width <= 0 || height <= 0) return null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(width, height));
            }

            bitmap.Save(path, ImageFormat.Png);
            return path;
        }
        catch (ExternalException)
        {
            // 屏幕捕获可能被安全策略整个禁掉；诊断失败不该升级成测试失败。
            return null;
        }
    }

    /// <summary>End the demo, and everything it started, now.</summary>
    internal void Kill()
    {
        _job?.Terminate();

        var process = _process;
        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }

        Launched.TryRemove(process.Id, out _);
        IsInKillOnCloseJob = false;
    }

    public void Dispose()
    {
        Kill();
        _job?.Dispose();
        _job = null;
        _process?.Dispose();
        _process = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The last line of defence: kill any demo this assembly started that is somehow still alive. Disposal on the way
    /// out should already have done it; this covers the host being torn down without running the cleanup.
    /// </summary>
    internal static void KillEverythingWeLaunched()
    {
        foreach (var (processId, executable) in Launched)
        {
            try
            {
                using var process = Process.GetProcessById(processId);

                // 只有名字还对得上才动手：pid 可能早已被系统回收给了别的进程。
                if (!string.Equals(process.ProcessName + ".exe", executable, StringComparison.OrdinalIgnoreCase)) continue;
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }

        Launched.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

/// <summary>
/// Registers the assembly-level teardown. It sits next to the host it cleans up rather than in <c>AssemblyInfo</c>, so
/// that the reason it exists is one screen away from the resource it releases.
/// </summary>
[TestClass]
public sealed class DemoProcessJanitor
{
    /// <summary>Kill any demo the suites started that is still running.</summary>
    [AssemblyCleanup]
    public static void KillLeakedDemos() => DesktopProcessHost.KillEverythingWeLaunched();
}
