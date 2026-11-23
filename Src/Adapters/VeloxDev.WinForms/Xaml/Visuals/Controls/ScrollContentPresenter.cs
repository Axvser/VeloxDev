using System.Drawing;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>Hosts one content element and clips it to a scrollable viewport.</summary>
/// <remarks>
/// <para>
/// The content is measured without a limit along each scrollable axis and against the viewport along a disabled
/// axis, so wrapped text reports its full width once horizontal scrolling is enabled while a long list reports its
/// full height. Arranging places the content at <c>-offset</c>; the clip stays on the viewport.
/// </para>
/// <para>
/// Offsets are clamped to <c>[0, Extent - Viewport]</c>. An axis whose <see cref="CanHorizontallyScroll"/> or
/// <see cref="CanVerticallyScroll"/> flag is <see langword="false"/> keeps a zero offset and ignores assignments.
/// </para>
/// </remarks>
public class ScrollContentPresenter : ContentPresenter, IScrollInfo
{
    private float _extentWidth;
    private float _extentHeight;
    private float _viewportWidth;
    private float _viewportHeight;
    private float _horizontalOffset;
    private float _verticalOffset;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll = true;
    private bool _isExternalScroll;

    // 横向不约束时的上限：够宽以容纳换行内容，又不会像 int.MaxValue 那样把虚拟列表的算术撑爆。
    private const int MaxUnboundedMeasure = 100_000;

    // 上一次对外公布的数值：只有真的变了才发 ScrollChanged，避免每次重排都打扰订阅者。
    private float _reportedHorizontalOffset;
    private float _reportedVerticalOffset;
    private float _reportedExtentWidth;
    private float _reportedExtentHeight;
    private float _reportedViewportWidth;
    private float _reportedViewportHeight;

    /// <summary>Creates an empty, vertically scrollable viewport.</summary>
    public ScrollContentPresenter()
    {
        IsHitTestVisible = true;
        Padding = XamlThickness.Empty;
    }

    /// <summary>Raised when an offset, the extent or the viewport changes.</summary>
    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    /// <summary>
    /// Whether the content is positioned by an external camera, so this presenter only measures the scroll range.
    /// </summary>
    /// <remarks>A camera host sizes the content to the world extent and applies its own transform; the presenter
    /// then keeps the content at the viewport origin while still publishing extent and viewport for the bars.</remarks>
    public bool IsExternalScroll
    {
        get => _isExternalScroll;
        set
        {
            if (_isExternalScroll == value)
            {
                return;
            }

            _isExternalScroll = value;
            InvalidateArrange();
        }
    }

    /// <summary>Whether the horizontal offset is honoured; when <see langword="false"/> it stays zero.</summary>
    public bool CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set
        {
            if (_canHorizontallyScroll == value)
            {
                return;
            }

            _canHorizontallyScroll = value;
            if (!value)
            {
                _horizontalOffset = 0f;
            }

            InvalidateMeasure();
        }
    }

    /// <summary>Whether the vertical offset is honoured; when <see langword="false"/> it stays zero.</summary>
    public bool CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set
        {
            if (_canVerticallyScroll == value)
            {
                return;
            }

            _canVerticallyScroll = value;
            if (!value)
            {
                _verticalOffset = 0f;
            }

            InvalidateMeasure();
        }
    }

    /// <summary>The full width of the content, excluding padding.</summary>
    public float ExtentWidth => _extentWidth;

    /// <summary>The full height of the content, excluding padding.</summary>
    public float ExtentHeight => _extentHeight;

    /// <summary>The visible width of the viewport, excluding padding.</summary>
    public float ViewportWidth => _viewportWidth;

    /// <summary>The visible height of the viewport, excluding padding.</summary>
    public float ViewportHeight => _viewportHeight;

    /// <summary>The largest valid <see cref="HorizontalOffset"/>.</summary>
    public float MaxHorizontalOffset => Math.Max(0f, _extentWidth - _viewportWidth);

    /// <summary>The largest valid <see cref="VerticalOffset"/>.</summary>
    public float MaxVerticalOffset => Math.Max(0f, _extentHeight - _viewportHeight);

    /// <summary>The horizontal scroll offset in pixels, clamped into the scrollable range.</summary>
    public float HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            var next = _canHorizontallyScroll
                ? Constrain(value, MaxHorizontalOffset, _extentWidth > 0f || _viewportWidth > 0f)
                : 0f;
            if (Math.Abs(next - _horizontalOffset) < 0.01f)
            {
                return;
            }

            _horizontalOffset = next;
            InvalidateArrange();
            RaiseScrollChanged();
        }
    }

    /// <summary>The vertical scroll offset in pixels, clamped into the scrollable range.</summary>
    public float VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            var next = _canVerticallyScroll
                ? Constrain(value, MaxVerticalOffset, _extentHeight > 0f || _viewportHeight > 0f)
                : 0f;
            if (Math.Abs(next - _verticalOffset) < 0.01f)
            {
                return;
            }

            _verticalOffset = next;
            InvalidateArrange();
            RaiseScrollChanged();
        }
    }

    /// <summary>Scrolls horizontally to <paramref name="offset"/>, clamped into the scrollable range.</summary>
    /// <param name="offset">The requested offset in pixels.</param>
    public void ScrollToHorizontalOffset(float offset) => HorizontalOffset = offset;

    /// <summary>Scrolls vertically to <paramref name="offset"/>, clamped into the scrollable range.</summary>
    /// <param name="offset">The requested offset in pixels.</param>
    public void ScrollToVerticalOffset(float offset) => VerticalOffset = offset;

    /// <summary>Scrolls both axes at once, each clamped independently.</summary>
    /// <param name="horizontalOffset">The requested horizontal offset in pixels.</param>
    /// <param name="verticalOffset">The requested vertical offset in pixels.</param>
    public void ScrollToOffsets(float horizontalOffset, float verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
    }

    /// <summary>The viewer that owns this presenter's scroll info, or <see langword="null"/>.</summary>
    public ScrollViewer? ScrollOwner { get; set; }

    // IScrollInfo 用 double；这家的偏移是 float，显式实现做一次收窄。
    double IScrollInfo.ExtentWidth => _extentWidth;

    double IScrollInfo.ExtentHeight => _extentHeight;

    double IScrollInfo.ViewportWidth => _viewportWidth;

    double IScrollInfo.ViewportHeight => _viewportHeight;

    double IScrollInfo.HorizontalOffset => _horizontalOffset;

    double IScrollInfo.VerticalOffset => _verticalOffset;

    void IScrollInfo.LineUp() => VerticalOffset -= 16f;

    void IScrollInfo.LineDown() => VerticalOffset += 16f;

    void IScrollInfo.LineLeft() => HorizontalOffset -= 16f;

    void IScrollInfo.LineRight() => HorizontalOffset += 16f;

    void IScrollInfo.PageUp() => VerticalOffset -= Math.Max(1f, _viewportHeight);

    void IScrollInfo.PageDown() => VerticalOffset += Math.Max(1f, _viewportHeight);

    void IScrollInfo.PageLeft() => HorizontalOffset -= Math.Max(1f, _viewportWidth);

    void IScrollInfo.PageRight() => HorizontalOffset += Math.Max(1f, _viewportWidth);

    void IScrollInfo.MouseWheelUp() => VerticalOffset -= 48f;

    void IScrollInfo.MouseWheelDown() => VerticalOffset += 48f;

    void IScrollInfo.MouseWheelLeft() => HorizontalOffset -= 48f;

    void IScrollInfo.MouseWheelRight() => HorizontalOffset += 48f;

    void IScrollInfo.SetHorizontalOffset(double offset) => HorizontalOffset = (float)offset;

    void IScrollInfo.SetVerticalOffset(double offset) => VerticalOffset = (float)offset;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var innerWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal));
        var innerHeight = Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical));

        if (PresentedContent is not { } content)
        {
            _extentWidth = 0f;
            _extentHeight = 0f;
            return new Size((int)Math.Ceiling(padding.Horizontal), (int)Math.Ceiling(padding.Vertical));
        }

        // 竖向不约束：长列表报出完整高度，同时按视口高实现行；横向只有开了滚动才不约束，
        // 否则长文本按视口换行。上限取一个大而有限的值，避免虚拟列表把 int.MaxValue 吃进算术。
        var measureHeight = _canVerticallyScroll ? int.MaxValue : innerHeight;
        Size desired;
        if (_canHorizontallyScroll)
        {
            var wide = content.Measure(new Size(MaxUnboundedMeasure, measureHeight));

            // 填满可用宽度的面板（虚拟列表）在未约束宽度下会原样报回这条上限；那会把横向 extent
            // 撑到十万像素并让横条永不消失，所以这类内容退回视口宽度重测。
            desired = wide.Width >= MaxUnboundedMeasure
                ? content.Measure(new Size(innerWidth, measureHeight))
                : wide;
        }
        else
        {
            desired = content.Measure(new Size(innerWidth, measureHeight));
        }

        _extentWidth = Math.Max(0f, desired.Width - (float)padding.Horizontal);
        _extentHeight = Math.Max(0f, desired.Height - (float)padding.Vertical);
        return new Size(
            (int)Math.Min(int.MaxValue, (long)desired.Width + (int)Math.Ceiling(padding.Horizontal)),
            (int)Math.Min(int.MaxValue, (long)desired.Height + (int)Math.Ceiling(padding.Vertical)));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        var viewport = new RectangleF(
            (int)Math.Ceiling(padding.Left),
            (int)Math.Ceiling(padding.Top),
            Math.Max(0f, finalSize.Width - (float)padding.Horizontal),
            Math.Max(0f, finalSize.Height - (float)padding.Vertical));
        _viewportWidth = viewport.Width;
        _viewportHeight = viewport.Height;
        Clip = viewport;

        _horizontalOffset = _canHorizontallyScroll
            ? XamlMath.Clamp(_horizontalOffset, 0f, MaxHorizontalOffset)
            : 0f;
        _verticalOffset = _canVerticallyScroll
            ? XamlMath.Clamp(_verticalOffset, 0f, MaxVerticalOffset)
            : 0f;

        if (PresentedContent is { } content && viewport.Width > 0f && viewport.Height > 0f)
        {
            var width = Math.Max(viewport.Width, _extentWidth);
            var height = Math.Max(viewport.Height, _extentHeight);
            var offsetX = _isExternalScroll ? 0f : _canHorizontallyScroll ? _horizontalOffset : 0f;
            var offsetY = _isExternalScroll ? 0f : _canVerticallyScroll ? _verticalOffset : 0f;
            content.Arrange(new RectangleF(
                viewport.Left - offsetX,
                viewport.Top - offsetY,
                width,
                height));
        }

        RaiseScrollChanged();
    }

    // 首次排版前 extent/viewport 还是 0；此时保留调用方给的偏移，等 Arrange 再按真实范围裁剪。
    private static float Constrain(float value, float maximum, bool known)
        => known ? XamlMath.Clamp(value, 0f, maximum) : value;

    // 变化量按 0.01 像素的容差比较，避免浮点重排把一个没动过的偏移误报成变化。
    private void RaiseScrollChanged()
    {
        var horizontalChange = _horizontalOffset - _reportedHorizontalOffset;
        var verticalChange = _verticalOffset - _reportedVerticalOffset;
        var extentWidthChange = _extentWidth - _reportedExtentWidth;
        var extentHeightChange = _extentHeight - _reportedExtentHeight;
        var viewportWidthChange = _viewportWidth - _reportedViewportWidth;
        var viewportHeightChange = _viewportHeight - _reportedViewportHeight;

        if (!Changed(horizontalChange)
            && !Changed(verticalChange)
            && !Changed(extentWidthChange)
            && !Changed(extentHeightChange)
            && !Changed(viewportWidthChange)
            && !Changed(viewportHeightChange))
        {
            return;
        }

        var args = new ScrollChangedEventArgs(
            _horizontalOffset,
            _verticalOffset,
            horizontalChange,
            verticalChange,
            _extentWidth,
            _extentHeight,
            extentWidthChange,
            extentHeightChange,
            _viewportWidth,
            _viewportHeight,
            viewportWidthChange,
            viewportHeightChange);

        _reportedHorizontalOffset = _horizontalOffset;
        _reportedVerticalOffset = _verticalOffset;
        _reportedExtentWidth = _extentWidth;
        _reportedExtentHeight = _extentHeight;
        _reportedViewportWidth = _viewportWidth;
        _reportedViewportHeight = _viewportHeight;

        ScrollChanged?.Invoke(this, args);
    }

    private static bool Changed(float delta) => Math.Abs(delta) >= 0.01f;
}

/// <summary>Carries the offsets, extents and viewport of a <see cref="ScrollContentPresenter"/> after a change.</summary>
public sealed class ScrollChangedEventArgs : EventArgs
{
    internal ScrollChangedEventArgs(
        float horizontalOffset,
        float verticalOffset,
        float horizontalChange,
        float verticalChange,
        float extentWidth,
        float extentHeight,
        float extentWidthChange,
        float extentHeightChange,
        float viewportWidth,
        float viewportHeight,
        float viewportWidthChange,
        float viewportHeightChange)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
        ExtentWidth = extentWidth;
        ExtentHeight = extentHeight;
        ExtentWidthChange = extentWidthChange;
        ExtentHeightChange = extentHeightChange;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        ViewportWidthChange = viewportWidthChange;
        ViewportHeightChange = viewportHeightChange;
    }

    /// <summary>The current horizontal offset.</summary>
    public float HorizontalOffset { get; }

    /// <summary>The current vertical offset.</summary>
    public float VerticalOffset { get; }

    /// <summary>The horizontal offset change since the previous report.</summary>
    public float HorizontalChange { get; }

    /// <summary>The vertical offset change since the previous report.</summary>
    public float VerticalChange { get; }

    /// <summary>The full content width.</summary>
    public float ExtentWidth { get; }

    /// <summary>The full content height.</summary>
    public float ExtentHeight { get; }

    /// <summary>The content width change since the previous report.</summary>
    public float ExtentWidthChange { get; }

    /// <summary>The content height change since the previous report.</summary>
    public float ExtentHeightChange { get; }

    /// <summary>The visible viewport width.</summary>
    public float ViewportWidth { get; }

    /// <summary>The visible viewport height.</summary>
    public float ViewportHeight { get; }

    /// <summary>The viewport width change since the previous report.</summary>
    public float ViewportWidthChange { get; }

    /// <summary>The viewport height change since the previous report.</summary>
    public float ViewportHeightChange { get; }
}
