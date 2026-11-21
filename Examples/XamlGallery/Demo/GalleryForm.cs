using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Demo.Views;
using VeloxDev.WinForms.Xaml;

namespace Demo;

/// <summary>The native window that hosts the XAML gallery view.</summary>
public sealed class GalleryForm : Form
{
    /// <summary>Creates the window and docks the gallery into it.</summary>
    /// <param name="args">Command-line arguments forwarded from <see cref="Program"/>.</param>
    public GalleryForm(string[] args)
    {
        Text = "VeloxDev WinForms XAML Gallery";
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(1024, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Current.WindowBackground;
        AutoScaleMode = AutoScaleMode.None;

        Controls.Add(new GalleryView { Dock = DockStyle.Fill });
        Theme.Changed += OnThemeChanged;
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    /// <inheritdoc />
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyTitleBarTheme();

        // 首帧里标题栏/分页的控件会先按未完成布局的尺寸画一遍；透明子窗口不会自己清掉旧位置。
        // 显示完再整窗重画一次，截图与用户看到的第一眼都是干净的一帧。
        BeginInvoke(new Action(() =>
        {
            if (!IsDisposed)
            {
                Invalidate(true);
                Update();
            }
        }));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            ApplyTitleBarTheme();
        }
    }

    // WinForms 的暗色模式不一定改 DWM 标题栏；显式按当前调色板设置，浅色模式才不会留一条黑标题栏。
    private void ApplyTitleBarTheme()
    {
        var dark = Theme.Mode == ThemeMode.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
