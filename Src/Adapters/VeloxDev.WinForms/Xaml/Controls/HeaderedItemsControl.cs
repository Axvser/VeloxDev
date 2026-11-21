using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// An <see cref="ItemsControl"/> with a header strip above the generated items.
/// </summary>
/// <remarks>
/// The strip is <see cref="HeaderHeight"/> pixels tall and reads its colours from <see cref="Theme.Current"/> unless
/// the author set an explicit background or foreground. Items start below the strip; virtualization and item
/// templates keep working unchanged.
/// </remarks>
public class HeaderedItemsControl : ItemsControl
{
    /// <summary>The height of the header strip in pixels.</summary>
    public const int HeaderHeight = 32;

    /// <summary>The text shown in the header strip.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(HeaderedItemsControl),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    /// <summary>Creates the control with the double-buffered, self-painted style it needs.</summary>
    public HeaderedItemsControl()
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

        var content = XamlLayout.ContentBounds(this);
        var shift = Math.Min(HeaderHeight, Math.Max(0, content.Height));
        if (shift <= 0)
        {
            return;
        }

        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (ReferenceEquals(child, ItemsPanel))
            {
                // 条目面板整体下移并让出表头高度；面板里的条目跟着面板走。
                var panelBounds = new Rectangle(
                    content.Left,
                    content.Top + shift,
                    content.Width,
                    Math.Max(0, content.Height - shift));
                if (child.Bounds != panelBounds)
                {
                    child.Bounds = panelBounds;
                }

                continue;
            }

            // 基类已经按 DisplayRectangle 摆好条目；表头占用的高度只能在这里统一下推一次。
            var bounds = child.Bounds;
            var shifted = new Rectangle(bounds.X, bounds.Y + shift, bounds.Width, bounds.Height);
            if (child.Bounds != shifted)
            {
                child.Bounds = shifted;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
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
        if (header.Width <= 0 || header.Height <= 0)
        {
            return;
        }

        using (var path = XamlDrawing.RoundedRectangle(
                   new RectangleF(header.Left, header.Top, header.Width, header.Height),
                   theme.CornerRadius))
        using (var brush = new SolidBrush(theme.SurfaceAlt))
        {
            e.Graphics.FillPath(brush, path);
        }

        using (var separator = new Pen(theme.ControlBorder))
        {
            e.Graphics.DrawLine(separator, header.Left, header.Bottom - 1, header.Right, header.Bottom - 1);
        }

        if (string.IsNullOrEmpty(Header) || header.Height <= 0)
        {
            return;
        }

        var foreground = HasXamlValue(ForeColorProperty) ? ForeColor : theme.Foreground;
        var textBounds = new Rectangle(header.Left + 12, header.Top, Math.Max(0, header.Width - 24), header.Height);
        TextRenderer.DrawText(
            e.Graphics,
            Header,
            Font,
            textBounds,
            foreground,
            TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
        if (target is HeaderedItemsControl control && !control.IsDisposed)
        {
            control.PerformLayout();
            control.Parent?.PerformLayout();
            control.Invalidate();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}
