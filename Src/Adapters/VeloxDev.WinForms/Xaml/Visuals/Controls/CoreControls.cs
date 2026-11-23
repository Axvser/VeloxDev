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
    /// <summary>The paint order among siblings; a lower value paints first (below).</summary>
    public static readonly DependencyProperty ZIndexProperty = DependencyProperty.RegisterAttached(
        "ZIndex", typeof(int), typeof(Panel), new PropertyMetadata(0));

    /// <summary>Creates a panel whose uncovered area does not claim pointer hits.</summary>
    protected Panel()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Reads the paint order of <paramref name="element"/>.</summary>
    public static int GetZIndex(VisualElement element)
        => element is null ? throw new ArgumentNullException(nameof(element)) : (int)element.GetValue(ZIndexProperty)!;

    /// <summary>Writes the paint order of <paramref name="element"/>.</summary>
    public static void SetZIndex(VisualElement element, int value)
        => (element ?? throw new ArgumentNullException(nameof(element))).SetValue(ZIndexProperty, value);

    /// <inheritdoc />
    /// <remarks>
    /// A non-null background is hit-testable even when its alpha is zero, matching WPF's
    /// <c>Background="Transparent"</c> behavior; a null background lets the pointer through.
    /// </remarks>
    protected override bool HitTestContent(PointF point)
        => Background is not null || base.HitTestContent(point);
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
    /// <remarks>
    /// Non-null brushes are hit-testable even at alpha zero, matching WPF; only a null brush passes through.
    /// </remarks>
    protected override bool HitTestContent(PointF point)
        => Background is not null
           || (BorderBrush is not null && !BorderThickness.IsEmpty);

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
                base.Add(value);
            }

            InvalidateMeasure();
        }
    }

    /// <summary>The child currently arranged by this border.</summary>
    protected VisualElement? ChildCore => _child;

    /// <summary>Adds the single child through <see cref="Child"/> so panels and XAML can treat every container alike.</summary>
    /// <param name="child">The child to add.</param>
    /// <typeparam name="T">The child's concrete type.</typeparam>
    /// <returns>The added child.</returns>
    public new T Add<T>(T child) where T : VisualElement
    {
        Child = child;
        return child;
    }

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
    /// <summary>Whether children stack vertically (default) or horizontally.</summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation), typeof(Orientation), typeof(StackPanel),
            new PropertyMetadata(Orientation.Vertical) { AffectsRender = false, AffectsMeasure = true });

    /// <summary>The gap between adjacent children in pixels.</summary>
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing), typeof(int), typeof(StackPanel),
            new PropertyMetadata(0) { AffectsRender = false, AffectsMeasure = true });

    /// <summary>Whether children stack vertically (default) or horizontally.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>The gap between adjacent children in pixels.</summary>
    public int Spacing
    {
        get => (int)GetValue(SpacingProperty)!;
        set => SetValue(SpacingProperty, value);
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
    /// <summary>The text to draw.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(TextBlock),
            new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The font used to draw the text.</summary>
    public static readonly DependencyProperty FontProperty =
        DependencyProperty.Register(
            nameof(Font), typeof(Font), typeof(TextBlock),
            new PropertyMetadata(DefaultFont) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The text colour as a brush value; an empty colour uses the active theme.</summary>
    /// <remarks>
    /// Stored values are canonicalised to a colour-comparing brush, so a <see cref="VisualTrigger"/> written as
    /// <c>Value="#FF203040"</c> matches a brush produced by a style, binding or local assignment.
    /// </remarks>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground), typeof(XamlBrush), typeof(TextBlock),
            new PropertyMetadata(null, null, static (_, value) => ColorBrushCoercion.Coerce(value))
            {
                AffectsRender = true,
            });

    /// <summary>Whether overlong text wraps at word boundaries.</summary>
    public static readonly DependencyProperty WrapProperty =
        DependencyProperty.Register(
            nameof(Wrap), typeof(bool), typeof(TextBlock),
            new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>How the text is aligned horizontally.</summary>
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(
            nameof(TextAlignment), typeof(XamlTextAlignment), typeof(TextBlock),
            new PropertyMetadata(XamlTextAlignment.Left) { AffectsRender = true });

    /// <summary>Creates a non-hit-testable text element.</summary>
    public TextBlock()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The text to draw.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty)!;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>The font used to draw the text, composed with the inherited typography properties.</summary>
    public Font Font
    {
        get => ResolveFont((Font)GetValue(FontProperty)!);
        set
        {
            if (value is not null)
            {
                SetValue(FontProperty, value);
            }
        }
    }

    /// <summary>The text colour; an empty colour uses the active theme.</summary>
    /// <remarks>
    /// The dependency property stores a brush, so a style, trigger or binding may supply a
    /// <see cref="VeloxDev.WinForms.Xaml.Brush"/>, a hex colour string or a <see cref="Color"/>. The CLR wrapper
    /// reads back the representative colour. A TwoWay binding writes the brush back to its source, so a source
    /// property must be brush-typed (or supply a converter); a <see cref="Color"/> source reports a binding failure
    /// on the write-back path instead of converting implicitly.
    /// </remarks>
    public Color Foreground
    {
        get => GetValue(ForegroundProperty) is XamlBrush brush ? brush.ApproximateColor : Color.Empty;
        set
        {
            if (value.IsEmpty)
            {
                ClearValue(ForegroundProperty);
            }
            else
            {
                SetValue(ForegroundProperty, new SolidColorBrush(value));
            }
        }
    }

    /// <summary>Whether overlong text wraps at word boundaries.</summary>
    public bool Wrap
    {
        get => (bool)GetValue(WrapProperty)!;
        set => SetValue(WrapProperty, value);
    }

    /// <summary>How the text is aligned horizontally.</summary>
    public XamlTextAlignment TextAlignment
    {
        get => (XamlTextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width > 0
            ? Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal))
            : 0;
        var sample = string.IsNullOrEmpty(Text) ? "M" : Text;
        var measured = VisualText.Measure(sample, Font, Wrap ? width : 0f, Wrap, RenderDpi);
        return new Size(
            (int)Math.Ceiling(measured.Width + Padding.Horizontal),
            (int)Math.Ceiling(measured.Height + Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (string.IsNullOrEmpty(Text))
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

        var color = Foreground.IsEmpty ? Theme.Current.Foreground : Foreground;
        var horizontal = TextAlignment switch
        {
            XamlTextAlignment.Center => StringAlignment.Center,
            XamlTextAlignment.Right => StringAlignment.Far,
            _ => StringAlignment.Near,
        };
        VisualText.Draw(this,
            graphics,
            Text,
            Font,
            color,
            bounds,
            Wrap,
            horizontal,
            Wrap ? StringAlignment.Near : StringAlignment.Center);
    }
}

/// <summary>Draws a bitmap inside the visual tree.</summary>
public class Image : VisualElement
{
    /// <summary>The bitmap or image to draw.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source), typeof(System.Drawing.Image), typeof(Image),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>How the source is fitted inside the bounds.</summary>
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(
            nameof(Stretch), typeof(XamlStretch), typeof(Image),
            new PropertyMetadata(XamlStretch.Uniform) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Creates a non-hit-testable image element.</summary>
    public Image()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The bitmap or image to draw.</summary>
    public System.Drawing.Image? Source
    {
        get => (System.Drawing.Image)GetValue(SourceProperty)!;
        set => SetValue(SourceProperty, value);
    }

    /// <summary>How the source is fitted inside the bounds.</summary>
    public XamlStretch Stretch
    {
        get => (XamlStretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => Source is null
            ? Size.Empty
            : new Size(Source.Width + (int)Math.Ceiling(Padding.Horizontal), Source.Height + (int)Math.Ceiling(Padding.Vertical));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (Source is null)
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

        var source = new RectangleF(0f, 0f, Source.Width, Source.Height);
        var box = destination;
        switch (Stretch)
        {
            case XamlStretch.None:
                destination = new RectangleF(destination.X, destination.Y, Source.Width, Source.Height);
                break;
            case XamlStretch.Uniform:
            case XamlStretch.UniformToFill:
            {
                var scale = Stretch == XamlStretch.Uniform
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
            if (Stretch == XamlStretch.UniformToFill)
            {
                graphics.SetClip(box, CombineMode.Intersect);
            }

            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(Source, destination, source, GraphicsUnit.Pixel);
        }
        finally
        {
            graphics.Restore(state);
        }
    }
}
