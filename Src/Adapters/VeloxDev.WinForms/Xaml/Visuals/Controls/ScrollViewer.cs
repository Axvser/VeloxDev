using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A single-child viewport that scrolls its content in two axes and clips it to the viewport.</summary>
/// <remarks>
/// <para>
/// <see cref="VerticalOffset"/> and <see cref="HorizontalOffset"/> move the content in the negative direction; the
/// viewport clip and hit-test bounds stay fixed. The self-drawn scrollbar thumbs overlay the content and are hit in
/// the viewer's own pointer handlers, so the pointer hit test of the content is unchanged.
/// </para>
/// <para>
/// A wheel turn scrolls vertically unless <see cref="Keys.Shift"/> is held (or only horizontal scrolling is
/// available). When the viewer has logical focus, <see cref="Keys.PageUp"/>, <see cref="Keys.PageDown"/>,
/// <see cref="Keys.Home"/> and <see cref="Keys.End"/> move the vertical offset.
/// </para>
/// </remarks>
public class ScrollViewer : VisualElement
{
    private const float BarThickness = 6f;
    private const float BarInset = 2f;
    private const float MinThumbLength = 24f;

    private VisualElement? _content;
    private float _verticalOffset;
    private float _horizontalOffset;
    private float _extentHeight;
    private float _extentWidth;
    private float _viewportHeight;
    private float _viewportWidth;
    private ScrollDrag _drag;
    private float _dragStartAxis;
    private float _dragStartOffset;

    /// <summary>Creates a focusable, hit-testable viewport.</summary>
    public ScrollViewer()
    {
        IsHitTestVisible = true;
        Focusable = true;
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
            _horizontalOffset = 0f;
            InvalidateMeasure();
        }
    }

    /// <summary>The vertical scroll offset in pixels.</summary>
    public float VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            var clamped = XamlMath.Clamp(value, 0f, MaxVerticalOffset);
            if (Math.Abs(clamped - _verticalOffset) < 0.01f)
            {
                return;
            }

            _verticalOffset = clamped;
            InvalidateArrange();
        }
    }

    /// <summary>The horizontal scroll offset in pixels.</summary>
    public float HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            var clamped = XamlMath.Clamp(value, 0f, MaxHorizontalOffset);
            if (Math.Abs(clamped - _horizontalOffset) < 0.01f)
            {
                return;
            }

            _horizontalOffset = clamped;
            InvalidateArrange();
        }
    }

    /// <summary>The full height of the content.</summary>
    public float ExtentHeight => _extentHeight;

    /// <summary>The full width of the content.</summary>
    public float ExtentWidth => _extentWidth;

    /// <summary>The visible height of the viewport.</summary>
    public float ViewportHeight => _viewportHeight;

    /// <summary>The visible width of the viewport.</summary>
    public float ViewportWidth => _viewportWidth;

    /// <summary>The largest valid <see cref="VerticalOffset"/>.</summary>
    public float MaxVerticalOffset => Math.Max(0f, _extentHeight - _viewportHeight);

    /// <summary>The largest valid <see cref="HorizontalOffset"/>.</summary>
    public float MaxHorizontalOffset => Math.Max(0f, _extentWidth - _viewportWidth);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var contentWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal));
        var desired = _content?.Measure(new Size(contentWidth, 0)) ?? Size.Empty;
        _extentHeight = Math.Max(0f, desired.Height - Padding.Vertical);
        _extentWidth = Math.Max(0f, desired.Width - Padding.Horizontal);
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
        _viewportWidth = viewport.Width;
        Clip = new RectangleF(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            viewport.Width,
            viewport.Height);

        if (_content is null || viewport.Width <= 0f || viewport.Height <= 0f)
        {
            return;
        }

        _verticalOffset = XamlMath.Clamp(_verticalOffset, 0f, MaxVerticalOffset);
        _horizontalOffset = XamlMath.Clamp(_horizontalOffset, 0f, MaxHorizontalOffset);

        var contentWidth = Math.Max(viewport.Width, _content.DesiredSize.Width - Padding.Horizontal);
        _content.Arrange(new RectangleF(
            viewport.Left - _horizontalOffset,
            viewport.Top - _verticalOffset,
            contentWidth,
            _content.DesiredSize.Height));
    }

    /// <inheritdoc />
    protected override void OnPointerWheel(VisualPointerEventArgs args)
    {
        var horizontal = (args.Modifiers & Keys.Shift) != 0;
        if (!horizontal && MaxVerticalOffset <= 0f && MaxHorizontalOffset > 0f)
        {
            // 只有横向可滚时不必再按住 Shift，滚轮直接横移。
            horizontal = true;
        }

        if (horizontal)
        {
            if (MaxHorizontalOffset <= 0f)
            {
                return;
            }

            HorizontalOffset -= args.Delta / 120f * 48f;
            args.Handled = true;
            return;
        }

        if (MaxVerticalOffset <= 0f)
        {
            return;
        }

        VerticalOffset -= args.Delta / 120f * 48f;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        var point = args.GetPosition(this);
        var verticalThumb = GetVerticalThumb();
        if (MaxVerticalOffset > 0f && verticalThumb.Contains(point))
        {
            _drag = ScrollDrag.Vertical;
            _dragStartAxis = point.Y;
            _dragStartOffset = _verticalOffset;
            args.Handled = true;
            return;
        }

        var horizontalThumb = GetHorizontalThumb();
        if (MaxHorizontalOffset > 0f && horizontalThumb.Contains(point))
        {
            _drag = ScrollDrag.Horizontal;
            _dragStartAxis = point.X;
            _dragStartOffset = _horizontalOffset;
            args.Handled = true;
            return;
        }

        var verticalTrack = GetVerticalTrack();
        if (MaxVerticalOffset > 0f && verticalTrack.Contains(point))
        {
            VerticalOffset += point.Y < verticalThumb.Top ? -_viewportHeight : _viewportHeight;
            args.Handled = true;
            return;
        }

        var horizontalTrack = GetHorizontalTrack();
        if (MaxHorizontalOffset > 0f && horizontalTrack.Contains(point))
        {
            HorizontalOffset += point.X < horizontalThumb.Left ? -_viewportWidth : _viewportWidth;
            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        if (_drag == ScrollDrag.None)
        {
            return;
        }

        var point = args.GetPosition(this);
        if (_drag == ScrollDrag.Vertical)
        {
            var travel = GetVerticalTrack().Height - GetVerticalThumb().Height;
            if (travel > 0.5f && MaxVerticalOffset > 0f)
            {
                VerticalOffset = _dragStartOffset + ((point.Y - _dragStartAxis) * MaxVerticalOffset / travel);
            }
        }
        else
        {
            var travel = GetHorizontalTrack().Width - GetHorizontalThumb().Width;
            if (travel > 0.5f && MaxHorizontalOffset > 0f)
            {
                HorizontalOffset = _dragStartOffset + ((point.X - _dragStartAxis) * MaxHorizontalOffset / travel);
            }
        }

        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        if (_drag != ScrollDrag.None)
        {
            _drag = ScrollDrag.None;
            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        switch (args.KeyCode)
        {
            case Keys.PageUp:
                VerticalOffset -= Math.Max(1f, _viewportHeight);
                args.Handled = true;
                break;
            case Keys.PageDown:
                VerticalOffset += Math.Max(1f, _viewportHeight);
                args.Handled = true;
                break;
            case Keys.Home:
                VerticalOffset = 0f;
                args.Handled = true;
                break;
            case Keys.End:
                VerticalOffset = MaxVerticalOffset;
                args.Handled = true;
                break;
        }
    }

    /// <summary>Scrolls the smallest distance that brings <paramref name="element"/> into the viewport.</summary>
    /// <param name="element">An element inside <see cref="Content"/>.</param>
    /// <remarks>
    /// The bounds are taken from <see cref="VisualElement.AbsoluteBounds"/>, so the viewer and the element must have
    /// been laid out at least once. An element already visible is left untouched.
    /// </remarks>
    public void ScrollIntoView(VisualElement element)
    {
        if (element is null || !IsInsideContent(element) || _viewportWidth <= 0f || _viewportHeight <= 0f)
        {
            return;
        }

        var origin = AbsoluteBounds.Location;
        var target = element.AbsoluteBounds;
        var relative = new RectangleF(
            target.X - origin.X,
            target.Y - origin.Y,
            target.Width,
            target.Height);
        var viewport = new RectangleF(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            _viewportWidth,
            _viewportHeight);

        if (relative.Top < viewport.Top)
        {
            VerticalOffset += relative.Top - viewport.Top;
        }
        else if (relative.Bottom > viewport.Bottom)
        {
            VerticalOffset += relative.Bottom - viewport.Bottom;
        }

        if (relative.Left < viewport.Left)
        {
            HorizontalOffset += relative.Left - viewport.Left;
        }
        else if (relative.Right > viewport.Right)
        {
            HorizontalOffset += relative.Right - viewport.Right;
        }
    }

    /// <summary>Brings <paramref name="element"/> into the viewport; an alias of <see cref="ScrollIntoView"/>.</summary>
    /// <param name="element">An element inside <see cref="Content"/>.</param>
    public void BringIntoView(VisualElement element) => ScrollIntoView(element);

    /// <inheritdoc />
    protected override void RenderChildren(Graphics graphics)
    {
        base.RenderChildren(graphics);
        var theme = Theme.Current;
        using var brush = new SolidBrush(Color.FromArgb(0x66, theme.MutedForeground));

        if (MaxVerticalOffset > 0f && _viewportHeight > 0f)
        {
            var thumb = GetVerticalThumb();
            if (thumb.Width > 0f && thumb.Height > 0f)
            {
                using var path = XamlDrawing.RoundedRectangle(thumb, 3f);
                graphics.FillPath(brush, path);
            }
        }

        if (MaxHorizontalOffset > 0f && _viewportWidth > 0f)
        {
            var thumb = GetHorizontalThumb();
            if (thumb.Width > 0f && thumb.Height > 0f)
            {
                using var path = XamlDrawing.RoundedRectangle(thumb, 3f);
                graphics.FillPath(brush, path);
            }
        }
    }

    private bool IsInsideContent(VisualElement element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, _content))
            {
                return true;
            }
        }

        return false;
    }

    private RectangleF GetVerticalTrack()
        => new(
            Math.Max(0f, Bounds.Width - Padding.Right - BarThickness - BarInset),
            Padding.Top,
            BarThickness,
            Math.Max(0f, _viewportHeight));

    private RectangleF GetVerticalThumb()
    {
        var track = GetVerticalTrack();
        if (track.Height <= 0f)
        {
            return RectangleF.Empty;
        }

        var ratio = _viewportHeight / Math.Max(_viewportHeight, _extentHeight);
        var length = Math.Min(track.Height, Math.Max(MinThumbLength, track.Height * ratio));
        var travel = Math.Max(0f, track.Height - length);
        var top = track.Top + (MaxVerticalOffset <= 0f ? 0f : (_verticalOffset / MaxVerticalOffset) * travel);
        return new RectangleF(track.X, top, track.Width, length);
    }

    private RectangleF GetHorizontalTrack()
        => new(
            Padding.Left,
            Math.Max(0f, Bounds.Height - Padding.Bottom - BarThickness - BarInset),
            Math.Max(0f, _viewportWidth),
            BarThickness);

    private RectangleF GetHorizontalThumb()
    {
        var track = GetHorizontalTrack();
        if (track.Width <= 0f)
        {
            return RectangleF.Empty;
        }

        var ratio = _viewportWidth / Math.Max(_viewportWidth, _extentWidth);
        var length = Math.Min(track.Width, Math.Max(MinThumbLength, track.Width * ratio));
        var travel = Math.Max(0f, track.Width - length);
        var left = track.Left + (MaxHorizontalOffset <= 0f ? 0f : (_horizontalOffset / MaxHorizontalOffset) * travel);
        return new RectangleF(left, track.Top, length, track.Height);
    }

    private enum ScrollDrag
    {
        None,
        Vertical,
        Horizontal,
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Surface?.RemoveNative(this);
        }

        base.Dispose(disposing);
    }
}
