using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;

namespace Demo;

/// <summary>The native window that hosts the visual-backend gallery surface.</summary>
public sealed class GalleryForm : Form
{
    private readonly GalleryModel _model = new();
    private GalleryVisualShell _shell;

    /// <summary>Creates the window and docks the single visual surface into it.</summary>
    /// <param name="args">Command-line arguments forwarded from <see cref="Program"/>.</param>
    public GalleryForm(string[] args)
    {
        _ = args;
        Text = "VeloxDev Visual Gallery";
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Current.WindowBackground;
        AutoScaleMode = AutoScaleMode.None;

        Surface = new VisualSurface
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Current.WindowBackground,
        };
        Controls.Add(Surface);

        _shell = new GalleryVisualShell(_model, ToggleTheme);
        Surface.Root = _shell.Root;
    }

    /// <summary>The single visual surface hosted by this window.</summary>
    public VisualSurface Surface { get; }

    /// <summary>The visual shell (catalog, preview, inspector).</summary>
    internal GalleryVisualShell Shell => _shell;

    /// <summary>Selects a card by id.</summary>
    /// <param name="id">The card id.</param>
    public bool SelectEntry(string id)
    {
        var selected = _shell.SelectEntry(id);
        Surface.InvalidateSurface();
        return selected;
    }

    /// <summary>Scrolls the preview viewport.</summary>
    /// <param name="offset">The vertical offset in pixels.</param>
    public void SetScrollOffset(float offset) => _shell.SetPreviewScrollOffset(offset);

    /// <summary>Hides the gallery chrome so only the preview is shown.</summary>
    /// <param name="visible">Whether the chrome is shown.</param>
    public void SetChromeVisible(bool visible) => _shell.SetChromeVisible(visible);

    /// <summary>Switches the palette; used by capture mode to verify the visual rebuild.</summary>
    public void ToggleThemeForCapture() => ToggleTheme();

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
        Surface.InvalidateSurface();
        BeginInvoke(new Action(() =>
        {
            if (!IsDisposed)
            {
                Surface.Refresh();
            }
        }));
    }

    private void ToggleTheme()
    {
        Theme.Mode = Theme.Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        Application.SetColorMode(Theme.Mode == ThemeMode.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        BackColor = Theme.Current.WindowBackground;
        Surface.BackColor = Theme.Current.WindowBackground;

        var previous = Surface.Root;
        var shell = new GalleryVisualShell(_model, ToggleTheme, _shell.SelectedId, _shell.FilterText);
        _shell = shell;
        Surface.Root = shell.Root;
        if (IsHandleCreated)
        {
            // 事件处理器可能就在被替换的树里；等这一手返回后再释放旧根。
            BeginInvoke(new Action(() => previous?.Dispose()));
        }
        else
        {
            previous?.Dispose();
        }

        Surface.InvalidateSurface();
        ApplyTitleBarTheme();
    }

    // WinForms 的暗色模式不一定改 DWM 标题栏；显式按当前调色板设置，浅色模式才不会留一条黑标题栏。
    private void ApplyTitleBarTheme()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var dark = Theme.Mode == ThemeMode.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
