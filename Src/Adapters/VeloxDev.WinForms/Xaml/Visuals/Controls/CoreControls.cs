using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlStretch = VeloxDev.WinForms.Xaml.Controls.Stretch;
using XamlTextAlignment = VeloxDev.WinForms.Xaml.Controls.TextAlignment;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>The base of elements that arrange children; empty space passes the pointer through.</summary>
public abstract class Panel : VisualElement
{
    /// <summary>Creates a panel whose uncovered area does not claim pointer hits.</summary>
    protected Panel()
    {
        IsHitTestVisible = false;
    }

    /// <inheritdoc />
    protected override bool HitTestContent(PointF point)
        => Background is { ApproximateColor.A: > 0 } || base.HitTestContent(point);
}

/// <summary>A container with a background, a rounded outline and one child.</summary>
public class Border : VisualElement
{
    private VisualElement? _child;

    /// <summary>Creates a border with theme corner radius.</summary>
    public Border()
    {
        IsHitTestVisible = false;
        CornerRadius = Theme.Current.CornerRadius;
        Padding = new XamlThickness(12f);
    }

    /// <inheritdoc />
    protected override bool HitTestContent(PointF point)
        => (Background is { ApproximateColor.A: > 0 })
           || (BorderBrush is { ApproximateColor.A: > 0 } && !BorderThickness.IsEmpty);

    /// <summary>The single child arranged inside the padding and border.</summary>
    public VisualElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            if (_child is not null)
            {
                Remove(_child);
            }

            _child = value;
            if (value is not null)
            {
                Add(value);
            }

            InvalidateMeasure();
        }
    }

    /// <summary>The child currently arranged by this border.</summary>
    protected VisualElement? ChildCore => _child;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var border = BorderThickness;
        var horizontal = (int)Math.Ceiling(padding.Horizontal + border.Horizontal);
        var vertical = (int)Math.Ceiling(padding.Vertical + border.Vertical);
        var inner = new Size(
            Math.Max(0, availableSize.Width - horizontal),
            Math.Max(0, availableSize.Height - vertical));
        var desired = _child?.Measure(inner) ?? Size.Empty;
        return new Size(desired.Width + horizontal, desired.Height + vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (_child is null)
        {
            return;
        }

        var padding = Padding;
        var border = BorderThickness;
        var content = new RectangleF(
            padding.Left + border.Left,
            padding.Top + border.Top,
            Math.Max(0f, finalSize.Width - padding.Horizontal - border.Horizontal),
            Math.Max(0f, finalSize.Height - padding.Vertical - border.Vertical));
        _child.Arrange(content);
    }
}

/// <summary>Stacks children vertically or horizontally with an explicit gap.</summary>
public class StackPanel : Panel
{
    private Orientation _orientation = Orientation.Vertical;
    private int _spacing;

    /// <summary>Whether children stack vertically (default) or horizontally.</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value)
            {
                return;
            }

            _orientation = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The gap between adjacent children in pixels.</summary>
    public int Spacing
    {
        get => _spacing;
        set
        {
            if (_spacing == value)
            {
                return;
            }

            _spacing = value;
            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var contentWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal));
        var contentHeight = Math.Max(0, availableSize.Height - (int)Math.Ceiling(Padding.Vertical));
        var main = 0;
        var cross = 0;
        var count = 0;
        foreach (var child in Children)
        {
            if (child is not VisualElement element || element.Visibility == XamlVisibility.Collapsed)
            {
                continue;
            }

            var measured = Orientation == Orientation.Vertical
                ? element.Measure(new Size(contentWidth, 0))
                : element.Measure(new Size(0, contentHeight));
            if (Orientation == Orientation.Vertical)
            {
                main += measured.Height;
                cross = Math.Max(cross, measured.Width);
            }
            else
            {
                main += measured.Width;
                cross = Math.Max(cross, measured.Height);
            }

            count++;
        }

        if (count > 1)
        {
            main += Math.Max(0, Spacing) * (count - 1);
        }

        return new Size(
            (Orientation == Orientation.Vertical ? cross : main) + (int)Math.Ceiling(Padding.Horizontal),
            (Orientation == Orientation.Vertical ? main : cross) + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var content = new RectangleF(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0f, finalSize.Width - Padding.Horizontal),
            Math.Max(0f, finalSize.Height - Padding.Vertical));
        var position = Orientation == Orientation.Vertical ? content.Top : content.Left;
        foreach (var child in Children)
        {
            if (child is not VisualElement element || element.Visibility == XamlVisibility.Collapsed)
            {
                continue;
            }

            var desired = element.DesiredSize;
            if (Orientation == Orientation.Vertical)
            {
                element.Arrange(new RectangleF(content.Left, position, content.Width, desired.Height));
                position += desired.Height;
            }
            else
            {
                element.Arrange(new RectangleF(position, content.Top, desired.Width, content.Height));
                position += desired.Width;
            }

            position += Math.Max(0, Spacing);
        }
    }
}

/// <summary>Draws wrapped or single-line text without creating a native label.</summary>
public class TextBlock : VisualElement
{
    private string _text = string.Empty;
    private Font _font = DefaultFont;
    private Color _foreground = Color.Empty;
    private bool _wrap;
    private XamlTextAlignment _textAlignment;

    /// <summary>Creates a non-hit-testable text element.</summary>
    public TextBlock()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The text to draw.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
            {
                return;
            }

            _text = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The font used to draw the text.</summary>
    public Font Font
    {
        get => _font;
        set
        {
            if (ReferenceEquals(_font, value) || value is null)
            {
                return;
            }

            _font = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The text colour; empty uses the active theme.</summary>
    public Color Foreground
    {
        get => _foreground;
        set
        {
            _foreground = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether overlong text wraps at word boundaries.</summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap == value)
            {
                return;
            }

            _wrap = value;
            InvalidateMeasure();
        }
    }

    /// <summary>How the text is aligned horizontally.</summary>
    public XamlTextAlignment TextAlignment
    {
        get => _textAlignment;
        set
        {
            _textAlignment = value;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width > 0
            ? Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal))
            : 0;
        var sample = string.IsNullOrEmpty(_text) ? "M" : _text;
        var measured = VisualText.Measure(sample, _font, _wrap ? width : 0f, _wrap, RenderDpi);
        return new Size(
            (int)Math.Ceiling(measured.Width + Padding.Horizontal),
            (int)Math.Ceiling(measured.Height + Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (string.IsNullOrEmpty(_text))
        {
            return;
        }

        var bounds = new Rectangle(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0, (int)Math.Ceiling(Bounds.Width - Padding.Horizontal)),
            Math.Max(0, (int)Math.Ceiling(Bounds.Height - Padding.Vertical)));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var color = _foreground.IsEmpty ? Theme.Current.Foreground : _foreground;
        var horizontal = _textAlignment switch
        {
            XamlTextAlignment.Center => StringAlignment.Center,
            XamlTextAlignment.Right => StringAlignment.Far,
            _ => StringAlignment.Near,
        };
        VisualText.Draw(
            graphics,
            _text,
            _font,
            color,
            bounds,
            _wrap,
            horizontal,
            _wrap ? StringAlignment.Near : StringAlignment.Center);
    }
}

/// <summary>Draws a bitmap inside the visual tree.</summary>
public class Image : VisualElement
{
    private System.Drawing.Image? _source;
    private XamlStretch _stretch = XamlStretch.Uniform;

    /// <summary>Creates a non-hit-testable image element.</summary>
    public Image()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The bitmap or image to draw.</summary>
    public System.Drawing.Image? Source
    {
        get => _source;
        set
        {
            _source = value;
            InvalidateMeasure();
        }
    }

    /// <summary>How the source is fitted inside the bounds.</summary>
    public XamlStretch Stretch
    {
        get => _stretch;
        set
        {
            _stretch = value;
            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => _source is null
            ? Size.Empty
            : new Size(_source.Width + (int)Math.Ceiling(Padding.Horizontal), _source.Height + (int)Math.Ceiling(Padding.Vertical));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (_source is null)
        {
            return;
        }

        var destination = new RectangleF(
            Padding.Left,
            Padding.Top,
            Math.Max(0f, Bounds.Width - Padding.Horizontal),
            Math.Max(0f, Bounds.Height - Padding.Vertical));
        if (destination.Width <= 0f || destination.Height <= 0f)
        {
            return;
        }

        var source = new RectangleF(0f, 0f, _source.Width, _source.Height);
        var box = destination;
        switch (_stretch)
        {
            case XamlStretch.None:
                destination = new RectangleF(destination.X, destination.Y, _source.Width, _source.Height);
                break;
            case XamlStretch.Uniform:
            case XamlStretch.UniformToFill:
            {
                var scale = _stretch == XamlStretch.Uniform
                    ? Math.Min(destination.Width / source.Width, destination.Height / source.Height)
                    : Math.Max(destination.Width / source.Width, destination.Height / source.Height);
                var width = source.Width * scale;
                var height = source.Height * scale;
                destination = new RectangleF(
                    destination.X + ((destination.Width - width) / 2f),
                    destination.Y + ((destination.Height - height) / 2f),
                    width,
                    height);
                break;
            }
        }

        var state = graphics.Save();
        try
        {
            if (_stretch == XamlStretch.UniformToFill)
            {
                graphics.SetClip(box, CombineMode.Intersect);
            }

            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(_source, destination, source, GraphicsUnit.Pixel);
        }
        finally
        {
            graphics.Restore(state);
        }
    }
}
