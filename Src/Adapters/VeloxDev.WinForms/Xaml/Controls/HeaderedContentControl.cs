using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A <see cref="ContentControl"/> with a header strip above its single content.
/// </summary>
/// <remarks>
/// The strip is <see cref="HeaderHeight"/> pixels tall and reads its colours from <see cref="Theme.Current"/> unless
/// the author set an explicit background or foreground. The content child fills the area below the strip.
/// </remarks>
public class HeaderedContentControl : ContentControl
{
    /// <summary>The height of the header strip in pixels.</summary>
    public const int HeaderHeight = 32;

    /// <summary>The text shown in the header strip.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(HeaderedContentControl),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    /// <summary>Creates the control with the double-buffered, self-painted style it needs.</summary>
    public HeaderedContentControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Gets or sets the text shown in the header strip.</summary>
    public string Header
    {
        get => (string?)GetValue(HeaderProperty) ?? string.Empty;
        set => SetValue(HeaderProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var content = base.GetPreferredSize(proposedSize);
        var measuredHeader = TextRenderer.MeasureText(
            Header,
            Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix);
        var padding = Padding;
        return new Size(
            Math.Max(content.Width, measuredHeader.Width + 24 + (int)Math.Ceiling(padding.Horizontal)),
            content.Height + HeaderHeight);
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (ContentVisual is not { } visual)
        {
            return;
        }

        var content = XamlLayout.ContentBounds(this);
        var shift = Math.Min(HeaderHeight, Math.Max(0, content.Height));
        var bounds = new Rectangle(
            content.Left,
            content.Top + shift,
            content.Width,
            Math.Max(0, content.Height - shift));
        if (visual is Control xaml)
        {
            XamlLayout.Arrange(xaml, bounds);
        }
        else if (visual.Bounds != bounds)
        {
            visual.Bounds = bounds;
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 先让基类铺父窗口底色，再盖自己的圆角表面；否则圆角外会留下系统灰。
        base.OnPaintBackground(e);

        var theme = Theme.Current;
        var background = HasXamlValue(BackColorProperty) ? Background : new SolidColorBrush(theme.Surface);
        XamlDrawing.PaintChrome(
            e.Graphics,
            ClientRectangle,
            background,
            theme.ControlBorder,
            new Thickness(1f),
            theme.CornerRadius);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!IsPainted)
        {
            return;
        }

        var theme = Theme.Current;
        var content = DisplayRectangle;
        var shift = Math.Min(HeaderHeight, Math.Max(0, content.Height));
        var header = new Rectangle(content.Left, content.Top, content.Width, shift);
        var foreground = HasXamlValue(ForeColorProperty) ? ForeColor : theme.Foreground;

        if (header.Width > 0 && header.Height > 0)
        {
            using var path = XamlDrawing.RoundedRectangle(
                new RectangleF(header.Left, header.Top, header.Width, header.Height),
                theme.CornerRadius);
            using var brush = new SolidBrush(theme.SurfaceAlt);
            e.Graphics.FillPath(brush, path);

            using var separator = new Pen(theme.ControlBorder);
            e.Graphics.DrawLine(separator, header.Left, header.Bottom - 1, header.Right, header.Bottom - 1);
        }

        if (!string.IsNullOrEmpty(Header) && header.Height > 0)
        {
            var textBounds = new Rectangle(header.Left + 12, header.Top, Math.Max(0, header.Width - 24), header.Height);
            TextRenderer.DrawText(
                e.Graphics,
                Header,
                Font,
                textBounds,
                foreground,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        if (ContentVisual is not null)
        {
            return;
        }

        var text = Content?.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var body = new Rectangle(
            content.Left,
            content.Top + shift,
            content.Width,
            Math.Max(0, content.Height - shift));
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            body,
            foreground,
            TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);
        if (ReferenceEquals(property, FontProperty) && !IsDisposed)
        {
            PerformLayout();
            Invalidate();
        }
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

    private static void OnHeaderPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is HeaderedContentControl control && !control.IsDisposed)
        {
            control.PerformLayout();
            control.Parent?.PerformLayout();
            control.Invalidate();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}
