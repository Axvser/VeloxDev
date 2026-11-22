using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A content control with a header strip and a rounded body, like a modern group box.
/// </summary>
/// <remarks>
/// The body, border and header colours come from <see cref="Theme.Current"/> when no explicit colour is set. The
/// single content child is placed below the header inside the control's padding.
/// </remarks>
public class GroupBox : ContentControl
{
    /// <summary>The text shown in the header strip.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(GroupBox),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    private const int HeaderHeight = 32;

    /// <summary>Creates the group box with the double-buffered, self-painted style it needs.</summary>
    public GroupBox()
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
        var header = TextRenderer.MeasureText(
            Header,
            Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix);
        var padding = Padding;
        return new Size(
            Math.Max(content.Width, header.Width + 24 + (int)Math.Ceiling(padding.Horizontal)),
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

        var content = DisplayRectangle;
        var bounds = new Rectangle(
            content.Left,
            content.Top + HeaderHeight,
            content.Width,
            Math.Max(0, content.Height - HeaderHeight));
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
        // 先让基类铺父窗口底色（圆角外的像素不能留系统灰），再画自己的圆角表面。
        base.OnPaintBackground(e);

        var theme = Theme.Current;
        var background = HasXamlValue(BackColorProperty) ? Background : new SolidColorBrush(theme.Surface);
        XamlDrawing.PaintChromeWithBrushes(
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
        var header = new Rectangle(content.Left, content.Top, content.Width, HeaderHeight);
        var foreground = HasXamlValue(ForeColorProperty) ? ForeColor : theme.Foreground;

        if (header.Width > 0 && header.Height > 0)
        {
            using var stripPath = XamlDrawing.RoundedRectangle(
                new RectangleF(header.Left, header.Top, header.Width, header.Height),
                theme.CornerRadius);
            using var stripBrush = new SolidBrush(theme.SurfaceAlt);
            e.Graphics.FillPath(stripBrush, stripPath);
        }

        if (!string.IsNullOrEmpty(Header))
        {
            var textBounds = new Rectangle(header.Left + 12, header.Top, Math.Max(0, header.Width - 24), header.Height);
            TextRenderer.DrawText(
                e.Graphics,
                Header,
                Font,
                textBounds,
                foreground,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            using var separator = new Pen(theme.ControlBorder);
            e.Graphics.DrawLine(
                separator,
                header.Left + 12,
                header.Bottom - 1,
                Math.Max(header.Left + 12, header.Right - 12),
                header.Bottom - 1);
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
            content.Top + HeaderHeight,
            content.Width,
            Math.Max(0, content.Height - HeaderHeight));
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
        if (target is GroupBox box && !box.IsDisposed)
        {
            box.PerformLayout();
            box.Parent?.PerformLayout();
            box.Invalidate();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}
