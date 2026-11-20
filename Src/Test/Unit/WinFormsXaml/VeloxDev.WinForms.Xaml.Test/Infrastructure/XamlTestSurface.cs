using System.Diagnostics;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Test.Infrastructure;

// WinForms 侧的无头宿主：只建句柄、不 Show。过渡系统的帧投递走 Control.BeginInvoke，
// 没有句柄时主机拒收，动画会静默地一帧都不写。
internal static class XamlTestSurface
{
    internal static void EnsureHandle(System.Windows.Forms.Control control)
    {
        _ = control.Handle;
        Application.DoEvents();
    }

    internal static bool PumpUntil(Func<bool> condition, int timeoutMilliseconds = 4000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            Application.DoEvents();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        Application.DoEvents();
        return condition();
    }

    internal static void Pump(int milliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }

        Application.DoEvents();
    }
}
