using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;

namespace Demo.Views;

/// <summary>The gallery shell: a themed header plus one tab per feature family.</summary>
public sealed partial class GalleryView : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private readonly GalleryModel _model = new();

    /// <summary>Creates the view, binds the model and applies the active palette.</summary>
    public GalleryView()
    {
        InitializeComponent();
        DataContext = _model;
        Font = new Font("Segoe UI", 9.5f);
        ApplyPalette();
        GalleryLayout.FitScrolledContent(this);
        Theme.Changed += OnThemeChanged;
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

    private void OnThemeToggleClick(object? sender, EventArgs e)
    {
        Theme.Mode = Theme.Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        ApplyPalette();
        Theme.ApplyTo(this);
        ThemeStyles.ApplyTo(this);
        Invalidate(true);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        // 原生控件（滚动条、下拉、树、网格）的暗色是 WinForms 按应用颜色模式画的，XAML 换调色板时一起换。
        Application.SetColorMode(Theme.Mode == ThemeMode.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        ApplyPalette();
        Invalidate(true);
    }

    // 调色板只落在一个地方：资源键被整体替换，树里的 DynamicResource 订阅会自己刷新。
    private void ApplyPalette()
    {
        var palette = Theme.Current;
        Resources["WindowBrush"] = new SolidColorBrush(palette.WindowBackground);
        Resources["HeaderBrush"] = ColorMix(palette.WindowBackground, palette.SurfaceAlt, 0.55f);
        Resources["SurfaceBrush"] = new SolidColorBrush(palette.Surface);
        Resources["CardBrush"] = ColorMix(palette.Surface, palette.SurfaceAlt, 0.32f);
        Resources["FieldBrush"] = new SolidColorBrush(palette.ControlBackground);
        Resources["BorderBrush"] = new SolidColorBrush(palette.ControlBorder);
        Resources["TextBrush"] = new SolidColorBrush(palette.Foreground);
        Resources["MutedBrush"] = new SolidColorBrush(palette.MutedForeground);
        Resources["AccentBrush"] = new SolidColorBrush(palette.Accent);
        Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(51, palette.Accent));
        Resources["SelectionBrush"] = new SolidColorBrush(palette.Selection);
        Resources["SuccessBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x3D, 0xD6, 0x8C));
        Resources["WarningBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xF2, 0xB4, 0x4B));
        Resources["DangerBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
        Resources["AccentGradient"] = CreateGradient(palette.Accent, ColorMix(palette.Accent, Color.Magenta, 0.35f));

        themeToggleButton.Content = Theme.Mode == ThemeMode.Dark ? "Light mode" : "Dark mode";
    }

    private static SolidColorBrush ColorMix(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new SolidColorBrush(Color.FromArgb(
            0xFF,
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount)));
    }

    private static LinearGradientBrush CreateGradient(Color start, Color end)
    {
        var brush = new LinearGradientBrush { StartPoint = new PointF(0f, 0f), EndPoint = new PointF(1f, 1f) };
        brush.GradientStops.Add(new GradientStop(start, 0f));
        brush.GradientStops.Add(new GradientStop(end, 1f));
        return brush;
    }
}

/// <summary>The type ramp shared by the shell and every section.</summary>
public static class GalleryTypography
{
    /// <summary>The application title.</summary>
    public static Font Title { get; } = new("Segoe UI Semibold", 19f, FontStyle.Bold);

    /// <summary>The header subtitle.</summary>
    public static Font Subtitle { get; } = new("Segoe UI", 9.75f);

    /// <summary>A section title.</summary>
    public static Font SectionTitle { get; } = new("Segoe UI Semibold", 16f, FontStyle.Bold);

    /// <summary>A card title.</summary>
    public static Font CardTitle { get; } = new("Segoe UI Semibold", 11f, FontStyle.Bold);

    /// <summary>The default body text.</summary>
    public static Font Body { get; } = new("Segoe UI", 9.5f);

    /// <summary>The monospace face used by code samples.</summary>
    public static Font Mono { get; } = new("Cascadia Mono", 9f);

    /// <summary>A compact label used by badges and readouts.</summary>
    public static Font Caption { get; } = new("Segoe UI", 8.75f);
}
