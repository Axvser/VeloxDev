using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.Platform;

// WinForms 的固定指标（标尺带、小地图、滚动条宽）按 96-DPI 逻辑单位书写；控件客户区是物理像素，
// 绘制/摆放前用这个比例换算。net461 没有 Control.DeviceDpi，退回窗口 DC 的 DPI。
internal static class WorkflowDpi
{
    internal static float Scale(Control control)
    {
        if (control is null || control.IsDisposed)
        {
            return 1f;
        }

#if NETFRAMEWORK
        if (!control.IsHandleCreated)
        {
            return 1f;
        }

        using var graphics = Graphics.FromHwnd(control.Handle);
        var dpi = graphics.DpiX;
#else
        var dpi = control.DeviceDpi;
#endif
        if (dpi <= 0)
        {
            return 1f;
        }

        var scale = dpi / 96f;
        return float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f ? 1f : scale;
    }

    internal static int Scale(Control control, int logical)
        => Math.Max(1, (int)Math.Round(logical * Scale(control)));
}
