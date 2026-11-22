using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>The direction the content of an <see cref="Expander"/> opens towards.</summary>
public enum ExpandDirection
{
    /// <summary>The header is at the top and the content opens downwards.</summary>
    Down = 0,

    /// <summary>The header is at the bottom and the content opens upwards.</summary>
    Up = 1,

    /// <summary>The header is on the right and the content opens to the left.</summary>
    Left = 2,

    /// <summary>The header is on the left and the content opens to the right.</summary>
    Right = 3,
}

/// <summary>
/// A content control whose content can be shown or hidden by clicking its header, like a WPF expander.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsExpanded"/> is a plain dependency property; there is no storyboard or transition. The chevron and the
/// header surface are painted from <see cref="Theme.Current"/>.
/// </para>
/// <para>Clicking the header or pressing Space/Enter while the control is focused toggles the expanded state.</para>
/// </remarks>
public class Expander : ContentControl
{
    /// <summary>The text shown in the header.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(Expander),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true });

    /// <summary>Whether the content is shown; collapsed by default.</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(Expander),
        new PropertyMetadata(false) { AffectsMeasure = true });

    /// <summary>The direction the content opens towards; downwards by default.</summary>
    public static readonly DependencyProperty ExpandDirectionProperty = DependencyProperty.Register(
        nameof(ExpandDirection),
        typeof(ExpandDirection),
        typeof(Expander),
        new PropertyMetadata(ExpandDirection.Down) { AffectsMeasure = true });

    private const int HeaderThickness = 32;
    private bool _hoverHeader;

    /// <summary>Creates the expander with the double-buffered, self-painted style it needs.</summary>
    public Expander()
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

    /// <summary>Gets or sets the text shown in the header.</summary>
    public string Header
    {
        get => (string?)GetValue(HeaderProperty) ?? string.Empty;
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Gets or sets whether the content is shown.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Gets or sets the direction the content opens towards.</summary>
    public ExpandDirection ExpandDirection
    {
        get => (ExpandDirection)GetValue(ExpandDirectionProperty)!;
        set => SetValue(ExpandDirectionProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var content = base.GetPreferredSize(proposedSize);
        var padding = Padding;
        var headerWidth = TextRenderer.MeasureText(
            Header,
            Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix).Width + 44;
        var horizontalPadding = (int)Math.Ceiling(padding.Horizontal);
        var verticalPadding = (int)Math.Ceiling(padding.Vertical);

        return ExpandDirection switch
        {
            ExpandDirection.Left or ExpandDirection.Right => IsExpanded
                ? new Size(content.Width + HeaderThickness, Math.Max(content.Height, HeaderThickness))
                : new Size(HeaderThickness + horizontalPadding, Math.Max(HeaderThickness + verticalPadding, content.Height)),
            _ => IsExpanded
                ? new Size(Math.Max(content.Width, headerWidth + horizontalPadding), content.Height + HeaderThickness)
                : new Size(Math.Max(content.Width, headerWidth + horizontalPadding), HeaderThickness + verticalPadding),
        };
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
        var header = GetHeaderBounds(content);
        var area = GetContentArea(content, header);
        var visible = IsExpanded && (visual is not Control xaml || xaml.Visibility == Visibility.Visible);

        // Visible 的 getter 会叠加父链状态；这里必须按本控件自己的取舍写入，否则父窗体不可见时永远读回 false。
        visual.Visible = visible;
        if (visible)
        {
            if (visual is Control xamlContent)
            {
                XamlLayout.Arrange(xamlContent, area);
            }
            else if (visual.Bounds != area)
            {
                visual.Bounds = area;
            }
        }
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
        var header = GetHeaderBounds(content);
        if (header.Width <= 0 || header.Height <= 0)
        {
            return;
        }

        var headerBackground = _hoverHeader ? theme.ControlHover : theme.SurfaceAlt;
        XamlDrawing.PaintChrome(
            e.Graphics,
            header,
            headerBackground,
            theme.ControlBorder,
            new Thickness(1f),
            theme.CornerRadius);

        var foreground = HasXamlValue(ForegroundProperty) ? EffectiveForeground : theme.Foreground;
        PaintChevron(e.Graphics, ChevronOrigin(header), _hoverHeader ? theme.Foreground : theme.MutedForeground);

        if (!string.IsNullOrEmpty(Header))
        {
            PaintHeaderText(e.Graphics, header, foreground);
        }

        if (ContentVisual is null && IsExpanded)
        {
            var text = Content?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    Font,
                    GetContentArea(content, header),
                    foreground,
                    TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !IsEnabled)
        {
            return;
        }

        if (!GetHeaderBounds(DisplayRectangle).Contains(e.Location))
        {
            return;
        }

        Focus();
        IsExpanded = !IsExpanded;
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = GetHeaderBounds(DisplayRectangle).Contains(e.Location);
        if (hover != _hoverHeader)
        {
            _hoverHeader = hover;
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverHeader)
        {
            _hoverHeader = false;
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Focused && IsEnabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
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

    private Rectangle GetHeaderBounds(Rectangle content)
    {
        var thickness = ExpandDirection switch
        {
            ExpandDirection.Left or ExpandDirection.Right => Math.Min(HeaderThickness, content.Width),
            _ => Math.Min(HeaderThickness, content.Height),
        };

        return ExpandDirection switch
        {
            ExpandDirection.Up => new Rectangle(content.Left, content.Bottom - thickness, content.Width, thickness),
            ExpandDirection.Left => new Rectangle(content.Right - thickness, content.Top, thickness, content.Height),
            ExpandDirection.Right => new Rectangle(content.Left, content.Top, thickness, content.Height),
            _ => new Rectangle(content.Left, content.Top, content.Width, thickness),
        };
    }

    private Rectangle GetContentArea(Rectangle content, Rectangle header)
    {
        return ExpandDirection switch
        {
            ExpandDirection.Up => new Rectangle(content.Left, content.Top, content.Width, Math.Max(0, header.Top - content.Top)),
            ExpandDirection.Left => new Rectangle(content.Left, content.Top, Math.Max(0, header.Left - content.Left), content.Height),
            ExpandDirection.Right => new Rectangle(header.Right, content.Top, Math.Max(0, content.Right - header.Right), content.Height),
            _ => new Rectangle(content.Left, header.Bottom, content.Width, Math.Max(0, content.Bottom - header.Bottom)),
        };
    }

    private Point ChevronOrigin(Rectangle header)
    {
        return ExpandDirection is ExpandDirection.Left or ExpandDirection.Right
            ? new Point(header.Left + (header.Width / 2), header.Top + 16)
            : new Point(header.Left + 16, header.Top + (header.Height / 2));
    }

    private void PaintHeaderText(Graphics graphics, Rectangle header, Color foreground)
    {
        if (ExpandDirection is ExpandDirection.Left or ExpandDirection.Right)
        {
            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform(header.Left + (header.Width / 2f), header.Top + (header.Height / 2f));
                graphics.RotateTransform(-90f);
                var available = Math.Max(0, header.Height - 28);
                var bounds = new RectangleF(-available / 2f, -Font.Height / 2f, available, Font.Height);
                using var brush = new SolidBrush(foreground);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap,
                };
                graphics.DrawString(Header, Font, brush, bounds, format);
            }
            finally
            {
                graphics.Restore(state);
            }

            return;
        }

        var textBounds = new Rectangle(header.Left + 28, header.Top, Math.Max(0, header.Width - 40), header.Height);
        TextRenderer.DrawText(
            graphics,
            Header,
            Font,
            textBounds,
            foreground,
            TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void PaintChevron(Graphics graphics, Point origin, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var direction = EffectiveChevronDirection();
        const float size = 4f;
        var centerX = origin.X;
        var centerY = origin.Y;
        PointF[] points = direction switch
        {
            ExpandDirection.Up => new[]
            {
                new PointF(centerX - size, centerY + (size / 2f)),
                new PointF(centerX, centerY - (size / 2f)),
                new PointF(centerX + size, centerY + (size / 2f)),
            },
            ExpandDirection.Left => new[]
            {
                new PointF(centerX + (size / 2f), centerY - size),
                new PointF(centerX - (size / 2f), centerY),
                new PointF(centerX + (size / 2f), centerY + size),
            },
            ExpandDirection.Right => new[]
            {
                new PointF(centerX - (size / 2f), centerY - size),
                new PointF(centerX + (size / 2f), centerY),
                new PointF(centerX - (size / 2f), centerY + size),
            },
            _ => new[]
            {
                new PointF(centerX - size, centerY - (size / 2f)),
                new PointF(centerX, centerY + (size / 2f)),
                new PointF(centerX + size, centerY - (size / 2f)),
            },
        };

        using var pen = new Pen(color, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawLines(pen, points);
    }

    private ExpandDirection EffectiveChevronDirection()
    {
        return (ExpandDirection, IsExpanded) switch
        {
            (ExpandDirection.Down, true) => ExpandDirection.Up,
            (ExpandDirection.Up, true) => ExpandDirection.Down,
            (ExpandDirection.Left, true) => ExpandDirection.Right,
            (ExpandDirection.Right, true) => ExpandDirection.Left,
            _ => ExpandDirection,
        };
    }


    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();
}
