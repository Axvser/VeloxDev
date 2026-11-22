using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A single-child viewport that scrolls its content vertically and clips it to the viewport.</summary>
public class ScrollViewer : VisualElement
{
    private VisualElement? _content;
    private float _verticalOffset;
    private float _extentHeight;
    private float _viewportHeight;

    /// <summary>Creates a hit-testable viewport.</summary>
    public ScrollViewer()
    {
        IsHitTestVisible = true;
        Padding = new XamlThickness(0f);
    }

    /// <summary>The scrolled content.</summary>
    public VisualElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            if (_content is not null)
            {
                Remove(_content);
            }

            _content = value;
            if (value is not null)
            {
                Add(value);
            }

            _verticalOffset = 0f;
            InvalidateMeasure();
        }
    }

    /// <summary>The vertical scroll offset in pixels.</summary>
    public float VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            var clamped = (float)Math.Max(0d, Math.Min(value, MaxVerticalOffset));
            if (Math.Abs(clamped - _verticalOffset) < 0.01f)
            {
                return;
            }

            _verticalOffset = clamped;
            InvalidateArrange();
        }
    }

    /// <summary>The full height of the content.</summary>
    public float ExtentHeight => _extentHeight;

    /// <summary>The visible height of the viewport.</summary>
    public float ViewportHeight => _viewportHeight;

    /// <summary>The largest valid <see cref="VerticalOffset"/>.</summary>
    public float MaxVerticalOffset => Math.Max(0f, _extentHeight - _viewportHeight);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var contentWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal));
        var desired = _content?.Measure(new Size(contentWidth, 0)) ?? Size.Empty;
        _extentHeight = Math.Max(0f, desired.Height - Padding.Vertical);
        return new Size(
            desired.Width + (int)Math.Ceiling(Padding.Horizontal),
            desired.Height + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var viewport = new RectangleF(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0f, finalSize.Width - Padding.Horizontal),
            Math.Max(0f, finalSize.Height - Padding.Vertical));
        _viewportHeight = viewport.Height;
        Clip = new RectangleF(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            viewport.Width,
            viewport.Height);

        if (_content is null || viewport.Width <= 0f || viewport.Height <= 0f)
        {
            return;
        }

        if (_verticalOffset > MaxVerticalOffset)
        {
            _verticalOffset = MaxVerticalOffset;
        }

        var contentWidth = Math.Max(viewport.Width, _content.DesiredSize.Width - Padding.Horizontal);
        _content.Arrange(new RectangleF(
            viewport.Left,
            viewport.Top - _verticalOffset,
            contentWidth,
            _content.DesiredSize.Height));
    }

    /// <inheritdoc />
    protected override void OnPointerWheel(VisualPointerEventArgs args)
    {
        if (MaxVerticalOffset <= 0f)
        {
            return;
        }

        VerticalOffset -= args.Delta / 120f * 48f;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void RenderChildren(Graphics graphics)
    {
        base.RenderChildren(graphics);
        if (MaxVerticalOffset <= 0f)
        {
            return;
        }

        var theme = Theme.Current;
        var trackHeight = Math.Max(0f, _viewportHeight);
        if (trackHeight <= 0f)
        {
            return;
        }

        var thumbHeight = Math.Max(24f, trackHeight * (_viewportHeight / Math.Max(_viewportHeight, _extentHeight)));
        var travel = Math.Max(0f, trackHeight - thumbHeight);
        var thumbTop = Padding.Top + (MaxVerticalOffset <= 0f ? 0f : (_verticalOffset / MaxVerticalOffset) * travel);
        var track = new RectangleF(
            Math.Max(0f, Bounds.Width - Padding.Right - 8f),
            Padding.Top,
            6f,
            trackHeight);
        var thumb = new RectangleF(track.X, thumbTop, track.Width, thumbHeight);
        using var path = XamlDrawing.RoundedRectangle(thumb, 3f);
        using var brush = new SolidBrush(Color.FromArgb(0x66, theme.MutedForeground));
        graphics.FillPath(brush, path);
    }
}

/// <summary>Hosts an ordinary WinForms control as a rectangular island above the visual surface.</summary>
/// <remarks>
/// The child is a real HWND and therefore always paints above the visual tree; it cannot be transformed, blended or
/// clipped by an arbitrary visual. Use it only for controls that need native behavior (editing, third-party hosts).
/// </remarks>
public class NativeControlHost : VisualElement
{
    private System.Windows.Forms.Control? _child;

    /// <summary>Creates a host that does not take pointer hits itself.</summary>
    public NativeControlHost()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The native control placed over this element's rectangle.</summary>
    public System.Windows.Forms.Control? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            Surface?.RemoveNative(this);
            _child = value;
            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_child is null)
        {
            return Size.Empty;
        }

        var preferred = _child.GetPreferredSize(System.Drawing.Size.Empty);
        return new Size(
            _child.Width > 0 ? _child.Width : preferred.Width,
            _child.Height > 0 ? _child.Height : preferred.Height);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (_child is null)
        {
            return;
        }

        Surface?.PlaceNative(this, _child, Rectangle.Round(AbsoluteBounds));
    }
}
