using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;
using XamlGridLength = VeloxDev.WinForms.Xaml.Controls.GridLength;
using XamlRowDefinition = VeloxDev.WinForms.Xaml.Controls.RowDefinition;
using XamlColumnDefinition = VeloxDev.WinForms.Xaml.Controls.ColumnDefinition;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A content control that scrolls its content through a presenter and two scrollbars.</summary>
/// <remarks>
/// <para>
/// The default chrome is a <see cref="ScrollContentPresenter"/> with a <see cref="ScrollBar"/> on each edge, so the
/// extent and viewport come from the content's own measurement exactly as in WPF. A caller-supplied
/// <see cref="ContentControl.Template"/> replaces that chrome; parts are resolved by name:
/// <c>PART_ScrollContentPresenter</c>, <c>PART_HorizontalScrollBar</c> and <c>PART_VerticalScrollBar</c>.
/// </para>
/// <para>
/// A camera host may turn on <see cref="IsExternalScroll"/>: the presenter then stops offsetting its content and
/// only reports extent and viewport; the host pushes offsets with <see cref="SetExternalOffset"/> and receives
/// gestures through <see cref="ScrollOffsetRequested"/>.
/// </para>
/// <para>
/// A wheel turn scrolls vertically unless <see cref="Keys.Shift"/> is held (or only horizontal scrolling is
/// possible). Page/Home/End move the vertical offset while the viewer has logical focus.
/// </para>
/// </remarks>
public class ScrollViewer : ContentControl
{
    private const float SmallStep = 16f;

    private static readonly VisualControlTemplate DefaultChrome = CreateDefaultChrome();

    private ScrollContentPresenter? _presenter;
    private ScrollBar? _verticalBar;
    private ScrollBar? _horizontalBar;
    private bool _externalScroll;
    private bool _updatingBars;
    private float _externalHorizontalOffset;
    private float _externalVerticalOffset;

    /// <summary>Whether the wheel and keyboard gestures scroll; a camera host may turn this off.</summary>
    public static readonly DependencyProperty IsScrollInputEnabledProperty = DependencyProperty.Register(
        nameof(IsScrollInputEnabled), typeof(bool), typeof(ScrollViewer),
        new PropertyMetadata(true) { AffectsRender = false });

    /// <summary>When the horizontal scrollbar is shown.</summary>
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollViewer),
        new PropertyMetadata(ScrollBarVisibility.Auto) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>When the vertical scrollbar is shown.</summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ScrollViewer),
        new PropertyMetadata(ScrollBarVisibility.Auto) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Whether the content's own <see cref="IScrollInfo"/> drives the offsets and steps.</summary>
    public static readonly DependencyProperty CanContentScrollProperty = DependencyProperty.Register(
        nameof(CanContentScroll), typeof(bool), typeof(ScrollViewer),
        new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Raised when an offset, the extent or the viewport changes.</summary>
    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    /// <summary>Raised when an external-scroll host should move its camera after a bar gesture.</summary>
    public event EventHandler<ScrollOffsetRequestedEventArgs>? ScrollOffsetRequested;

    /// <summary>Creates the viewer and its default presenter/scrollbar chrome.</summary>
    public ScrollViewer()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = XamlThickness.Empty;
        ReapplyTemplate();
        AttachParts();
    }

    /// <summary>Whether the wheel and keyboard gestures scroll the content.</summary>
    public bool IsScrollInputEnabled
    {
        get => (bool)GetValue(IsScrollInputEnabledProperty)!;
        set => SetValue(IsScrollInputEnabledProperty, value);
    }

    /// <summary>When the horizontal scrollbar is shown.</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>When the vertical scrollbar is shown.</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>Whether the content's own <see cref="IScrollInfo"/> drives the offsets and steps.</summary>
    public bool CanContentScroll
    {
        get => (bool)GetValue(CanContentScrollProperty)!;
        set => SetValue(CanContentScrollProperty, value);
    }

    /// <summary>The presenter part that hosts the content.</summary>
    public ScrollContentPresenter? ContentPresenter => _presenter;

    /// <summary>The vertical scrollbar part, or <see langword="null"/> when the template has none.</summary>
    public ScrollBar? VerticalScrollBar => _verticalBar;

    /// <summary>The horizontal scrollbar part, or <see langword="null"/> when the template has none.</summary>
    public ScrollBar? HorizontalScrollBar => _horizontalBar;

    /// <summary>Whether an external camera supplies the offsets instead of this viewer's own presenter.</summary>
    public bool IsExternalScroll
    {
        get => _externalScroll;
        set
        {
            if (_externalScroll == value)
            {
                return;
            }

            _externalScroll = value;
            if (_presenter is not null)
            {
                _presenter.IsExternalScroll = value;
            }

            UpdateBars();
        }
    }

    /// <summary>The horizontal scroll offset in pixels.</summary>
    public float HorizontalOffset
    {
        get => _externalScroll ? _externalHorizontalOffset : (float)(EffectiveScrollInfo?.HorizontalOffset ?? 0d);
        set
        {
            if (_externalScroll)
            {
                _externalHorizontalOffset = value;
                UpdateBars();
                return;
            }

            EffectiveScrollInfo?.SetHorizontalOffset(value);
            UpdateBars();
        }
    }

    /// <summary>The vertical scroll offset in pixels.</summary>
    public float VerticalOffset
    {
        get => _externalScroll ? _externalVerticalOffset : (float)(EffectiveScrollInfo?.VerticalOffset ?? 0d);
        set
        {
            if (_externalScroll)
            {
                _externalVerticalOffset = value;
                UpdateBars();
                return;
            }

            EffectiveScrollInfo?.SetVerticalOffset(value);
            UpdateBars();
        }
    }

    /// <summary>The full width of the content.</summary>
    public float ExtentWidth => (float)(EffectiveScrollInfo?.ExtentWidth ?? 0d);

    /// <summary>The full height of the content.</summary>
    public float ExtentHeight => (float)(EffectiveScrollInfo?.ExtentHeight ?? 0d);

    /// <summary>The visible width of the viewport.</summary>
    public float ViewportWidth => (float)(EffectiveScrollInfo?.ViewportWidth ?? 0d);

    /// <summary>The visible height of the viewport.</summary>
    public float ViewportHeight => (float)(EffectiveScrollInfo?.ViewportHeight ?? 0d);

    /// <summary>The largest valid <see cref="HorizontalOffset"/>.</summary>
    public float MaxHorizontalOffset => Math.Max(0f, ExtentWidth - ViewportWidth);

    /// <summary>The largest valid <see cref="VerticalOffset"/>.</summary>
    public float MaxVerticalOffset => Math.Max(0f, ExtentHeight - ViewportHeight);

    /// <summary>Notifies the viewer that the content's scroll info changed.</summary>
    /// <remarks>A content that implements <see cref="IScrollInfo"/> calls this after it moves its own offset.</remarks>
    public void InvalidateScrollInfo() => UpdateBars();

    // CanContentScroll 时优先用内容自己的 IScrollInfo；否则 presenter 自己就是像素级滚动信息。
    private IScrollInfo? EffectiveScrollInfo
        => _presenter is not { } presenter
            ? null
            : CanContentScroll && presenter.PresentedContent is IScrollInfo content
                ? content
                : presenter;

    /// <summary>Scrolls the smallest distance that brings <paramref name="element"/> into the viewport.</summary>
    /// <param name="element">An element inside the presented content.</param>
    public void ScrollIntoView(VisualElement element)
    {
        if (_presenter is null || element is null || _presenter.PresentedContent is not { } content)
        {
            return;
        }

        // 逻辑滚动由内容自己定位；这里只处理像素 presenter。
        if (!ReferenceEquals(EffectiveScrollInfo, _presenter))
        {
            return;
        }

        if (!IsInside(element, content))
        {
            return;
        }

        var origin = _presenter.AbsoluteBounds;
        var target = element.AbsoluteBounds;
        var relative = new RectangleF(target.X - origin.X, target.Y - origin.Y, target.Width, target.Height);
        var viewport = new RectangleF(0f, 0f, _presenter.ViewportWidth, _presenter.ViewportHeight);

        var horizontal = HorizontalOffset;
        if (relative.Left < viewport.Left)
        {
            horizontal += relative.Left - viewport.Left;
        }
        else if (relative.Right > viewport.Right)
        {
            horizontal += relative.Right - viewport.Right;
        }

        var vertical = VerticalOffset;
        if (relative.Top < viewport.Top)
        {
            vertical += relative.Top - viewport.Top;
        }
        else if (relative.Bottom > viewport.Bottom)
        {
            vertical += relative.Bottom - viewport.Bottom;
        }

        if (_externalScroll)
        {
            _externalHorizontalOffset = horizontal;
            _externalVerticalOffset = vertical;
            UpdateBars();
            ScrollOffsetRequested?.Invoke(this, new ScrollOffsetRequestedEventArgs(horizontal, vertical));
            return;
        }

        _presenter.ScrollToOffsets(horizontal, vertical);
    }

    /// <summary>Brings <paramref name="element"/> into the viewport; an alias of <see cref="ScrollIntoView"/>.</summary>
    /// <param name="element">An element inside the presented content.</param>
    public void BringIntoView(VisualElement element) => ScrollIntoView(element);

    /// <summary>Publishes the offset an external-scroll host is showing.</summary>
    /// <param name="horizontalOffset">The horizontal offset in surface pixels.</param>
    /// <param name="verticalOffset">The vertical offset in surface pixels.</param>
    public void SetExternalOffset(float horizontalOffset, float verticalOffset)
    {
        if (Math.Abs(horizontalOffset - _externalHorizontalOffset) < 0.01f
            && Math.Abs(verticalOffset - _externalVerticalOffset) < 0.01f)
        {
            return;
        }

        _externalHorizontalOffset = horizontalOffset;
        _externalVerticalOffset = verticalOffset;
        UpdateBars();
    }

    /// <summary>Returns whether <paramref name="localPoint"/> lies on either scrollbar's track.</summary>
    /// <param name="localPoint">A point in this viewer's local coordinates.</param>
    public bool IsOverScrollBar(PointF localPoint)
    {
        var surfacePoint = PointToSurface(localPoint);
        return (_verticalBar is { } vertical && vertical.IsOverTrack(vertical.PointFromSurface(surfacePoint)))
               || (_horizontalBar is { } horizontal && horizontal.IsOverTrack(horizontal.PointFromSurface(surfacePoint)));
    }

    /// <inheritdoc />
    protected override VisualControlTemplate? DefaultTemplate => DefaultChrome;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // 模板里的 Grid 会按可用尺寸铺满；ScrollViewer 自己的 desired 必须回到内容尺寸，
        // 否则放进 Auto 布局（下拉列表弹层、StackPanel）时会把整条可用宽度都占掉。
        var fallback = base.MeasureOverride(availableSize);
        if (_presenter is not { } presenter)
        {
            return fallback;
        }

        var padding = Padding;
        var extraWidth = _verticalBar is { Visibility: XamlVisibility.Visible } vertical
            ? vertical.DesiredSize.Width
            : 0;
        var extraHeight = _horizontalBar is { Visibility: XamlVisibility.Visible } horizontal
            ? horizontal.DesiredSize.Height
            : 0;
        return new Size(
            (int)Math.Min(int.MaxValue, (long)presenter.DesiredSize.Width + extraWidth + (int)Math.Ceiling(padding.Horizontal)),
            (int)Math.Min(int.MaxValue, (long)presenter.DesiredSize.Height + extraHeight + (int)Math.Ceiling(padding.Vertical)));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(e.Property, TemplateProperty))
        {
            DetachParts();
            base.OnPropertyChanged(e);
            AttachParts();
            return;
        }

        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, HorizontalScrollBarVisibilityProperty)
            || ReferenceEquals(e.Property, VerticalScrollBarVisibilityProperty)
            || ReferenceEquals(e.Property, CanContentScrollProperty))
        {
            UpdateBars();
        }
    }

    /// <inheritdoc />
    internal override void ApplyStyleTemplate(VisualControlTemplate? template)
    {
        DetachParts();
        base.ApplyStyleTemplate(template);
        AttachParts();
    }

    /// <inheritdoc />
    protected override void OnPointerWheel(VisualPointerEventArgs args)
    {
        if (!IsScrollInputEnabled || _externalScroll || _presenter is null)
        {
            return;
        }

        var horizontal = (args.Modifiers & Keys.Shift) != 0;
        if (!horizontal && MaxVerticalOffset <= 0f && MaxHorizontalOffset > 0f)
        {
            // 只有横向可滚时不必再按住 Shift，滚轮直接横移。
            horizontal = true;
        }

        var info = EffectiveScrollInfo;
        if (info is null)
        {
            return;
        }

        var notches = Math.Max(1, Math.Abs(args.Delta) / 120);
        if (horizontal)
        {
            if (MaxHorizontalOffset <= 0f)
            {
                return;
            }

            for (var index = 0; index < notches; index++)
            {
                if (args.Delta > 0)
                {
                    info.MouseWheelLeft();
                }
                else
                {
                    info.MouseWheelRight();
                }
            }
        }
        else
        {
            if (MaxVerticalOffset <= 0f)
            {
                return;
            }

            for (var index = 0; index < notches; index++)
            {
                if (args.Delta > 0)
                {
                    info.MouseWheelUp();
                }
                else
                {
                    info.MouseWheelDown();
                }
            }
        }

        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (_externalScroll || EffectiveScrollInfo is not { } info)
        {
            return;
        }

        switch (args.KeyCode)
        {
            case Keys.PageUp:
                info.PageUp();
                args.Handled = true;
                break;
            case Keys.PageDown:
                info.PageDown();
                args.Handled = true;
                break;
            case Keys.Home:
                info.SetVerticalOffset(0d);
                args.Handled = true;
                break;
            case Keys.End:
                info.SetVerticalOffset(MaxVerticalOffset);
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachParts();
        }

        base.Dispose(disposing);
    }

    // 模板（默认 chrome 或调用方提供的 Template）换成新实例后，按部件名重新接线。
    private void AttachParts()
    {
        var root = TemplateRoot;
        _presenter = GetTemplateChild<ScrollContentPresenter>("PART_ScrollContentPresenter")
                     ?? FindFirst<ScrollContentPresenter>(root);
        _verticalBar = GetTemplateChild<ScrollBar>("PART_VerticalScrollBar")
                       ?? FindBar(root, Orientation.Vertical);
        _horizontalBar = GetTemplateChild<ScrollBar>("PART_HorizontalScrollBar")
                         ?? FindBar(root, Orientation.Horizontal);

        if (_presenter is not null)
        {
            _presenter.IsExternalScroll = _externalScroll;
            _presenter.ScrollChanged += OnPresenterScrollChanged;
        }

        if (_verticalBar is not null)
        {
            _verticalBar.Scroll += OnVerticalBarScroll;
        }

        if (_horizontalBar is not null)
        {
            _horizontalBar.Scroll += OnHorizontalBarScroll;
        }

        UpdateBars();
    }

    private void DetachParts()
    {
        if (_presenter is not null)
        {
            _presenter.ScrollChanged -= OnPresenterScrollChanged;
        }

        if (_verticalBar is not null)
        {
            _verticalBar.Scroll -= OnVerticalBarScroll;
        }

        if (_horizontalBar is not null)
        {
            _horizontalBar.Scroll -= OnHorizontalBarScroll;
        }

        _presenter = null;
        _verticalBar = null;
        _horizontalBar = null;
    }

    private void OnPresenterScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateBars();
        ScrollChanged?.Invoke(this, e);
    }

    private void OnVerticalBarScroll(object? sender, ScrollBarScrollEventArgs e)
    {
        if (_externalScroll)
        {
            _externalVerticalOffset = (float)e.NewValue;
            ScrollOffsetRequested?.Invoke(
                this,
                new ScrollOffsetRequestedEventArgs(_externalHorizontalOffset, _externalVerticalOffset));
            return;
        }

        EffectiveScrollInfo?.SetVerticalOffset(e.NewValue);
    }

    private void OnHorizontalBarScroll(object? sender, ScrollBarScrollEventArgs e)
    {
        if (_externalScroll)
        {
            _externalHorizontalOffset = (float)e.NewValue;
            ScrollOffsetRequested?.Invoke(
                this,
                new ScrollOffsetRequestedEventArgs(_externalHorizontalOffset, _externalVerticalOffset));
            return;
        }

        EffectiveScrollInfo?.SetHorizontalOffset(e.NewValue);
    }

    // 把滚动信息（presenter 或内容自己的 IScrollInfo）的范围/视口/偏移抄给两条滚动条。
    private void UpdateBars()
    {
        if (_updatingBars || _presenter is not { } presenter)
        {
            return;
        }

        var info = EffectiveScrollInfo;
        if (info is null)
        {
            return;
        }

        _updatingBars = true;
        var barsChanged = false;
        try
        {
            var logical = !ReferenceEquals(info, presenter);
            var extentWidth = (float)info.ExtentWidth;
            var extentHeight = (float)info.ExtentHeight;
            var viewportWidth = (float)info.ViewportWidth;
            var viewportHeight = (float)info.ViewportHeight;
            var horizontalVisible = ShouldShow(HorizontalScrollBarVisibility, extentWidth, viewportWidth);
            var verticalVisible = ShouldShow(VerticalScrollBarVisibility, extentHeight, viewportHeight);

            info.ScrollOwner = this;
            info.CanHorizontallyScroll = HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
            info.CanVerticallyScroll = VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
            presenter.ScrollOwner = this;

            if (logical)
            {
                // 内容自己处理单位与偏移；presenter 只负责测量和裁剪。
                presenter.CanHorizontallyScroll = false;
                presenter.CanVerticallyScroll = false;
                if (Math.Abs(presenter.HorizontalOffset) > 0.01f || Math.Abs(presenter.VerticalOffset) > 0.01f)
                {
                    presenter.ScrollToOffsets(0f, 0f);
                }
            }

            if (_horizontalBar is { } horizontal)
            {
                horizontal.Minimum = 0d;
                horizontal.Maximum = extentWidth;
                horizontal.ViewportSize = viewportWidth;
                horizontal.SmallChange = SmallStep;
                horizontal.LargeChange = Math.Max(1d, viewportWidth);
                horizontal.Value = _externalScroll ? _externalHorizontalOffset : (float)info.HorizontalOffset;
                horizontal.ReserveCornerSize = 0d;
                var target = horizontalVisible ? XamlVisibility.Visible : XamlVisibility.Collapsed;
                if (horizontal.Visibility != target)
                {
                    horizontal.Visibility = target;
                    barsChanged = true;
                }
            }

            if (_verticalBar is { } vertical)
            {
                vertical.Minimum = 0d;
                vertical.Maximum = extentHeight;
                vertical.ViewportSize = viewportHeight;
                vertical.SmallChange = SmallStep;
                vertical.LargeChange = Math.Max(1d, viewportHeight);
                vertical.Value = _externalScroll ? _externalVerticalOffset : (float)info.VerticalOffset;
                vertical.ReserveCornerSize = 0d;
                var target = verticalVisible ? XamlVisibility.Visible : XamlVisibility.Collapsed;
                if (vertical.Visibility != target)
                {
                    vertical.Visibility = target;
                    barsChanged = true;
                }
            }
        }
        finally
        {
            _updatingBars = false;
        }

        // Visibility 变化发生在 presenter 的 Arrange 里；父级缓存不会自动因为子元素折叠/展开而重排。
        if (barsChanged)
        {
            TemplateRoot?.InvalidateMeasure();
            InvalidateMeasure();
        }
    }

    private static bool ShouldShow(ScrollBarVisibility visibility, float extent, float viewport)
        => visibility switch
        {
            ScrollBarVisibility.Visible => true,
            ScrollBarVisibility.Auto => extent > viewport + 0.5f,
            _ => false,
        };

    private static bool IsInside(VisualElement element, VisualElement content)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, content))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindFirst<T>(VisualElement? root) where T : VisualElement
    {
        if (root is null)
        {
            return null;
        }

        if (root is T match)
        {
            return match;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindFirst<T>(element) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static ScrollBar? FindBar(VisualElement? root, Orientation orientation)
        => FindFirstBar(root, orientation);

    private static ScrollBar? FindFirstBar(VisualElement? root, Orientation orientation)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ScrollBar bar && bar.Orientation == orientation)
        {
            return bar;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindFirstBar(element, orientation) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static VisualControlTemplate CreateDefaultChrome()
        => new(static scope =>
        {
            // WPF 的默认组织：presenter 占主格，滚动条各占一条 Auto 轨道，互不覆盖。
            var root = new Grid();
            root.RowDefinitions.Add(new XamlRowDefinition { Height = XamlGridLength.Star });
            root.RowDefinitions.Add(new XamlRowDefinition { Height = XamlGridLength.Auto });
            root.ColumnDefinitions.Add(new XamlColumnDefinition { Width = XamlGridLength.Star });
            root.ColumnDefinitions.Add(new XamlColumnDefinition { Width = XamlGridLength.Auto });

            var presenter = new ScrollContentPresenter { Name = "PART_ScrollContentPresenter" };
            scope.Register("PART_ScrollContentPresenter", presenter);
            Grid.SetRow(presenter, 0);
            Grid.SetColumn(presenter, 0);

            var vertical = new ScrollBar
            {
                Name = "PART_VerticalScrollBar",
                Orientation = Orientation.Vertical,
                HorizontalAlignment = XamlHorizontalAlignment.Stretch,
                VerticalAlignment = XamlVerticalAlignment.Stretch,
            };
            scope.Register("PART_VerticalScrollBar", vertical);
            Grid.SetRow(vertical, 0);
            Grid.SetColumn(vertical, 1);

            var horizontal = new ScrollBar
            {
                Name = "PART_HorizontalScrollBar",
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = XamlHorizontalAlignment.Stretch,
                VerticalAlignment = XamlVerticalAlignment.Stretch,
            };
            scope.Register("PART_HorizontalScrollBar", horizontal);
            Grid.SetRow(horizontal, 1);
            Grid.SetColumn(horizontal, 0);

            root.Add(presenter);
            root.Add(vertical);
            root.Add(horizontal);
            return root;
        })
        {
            TargetType = typeof(ScrollViewer),
        };
}

/// <summary>When a scrollbar is shown for its axis.</summary>
public enum ScrollBarVisibility
{
    /// <summary>Never shown, and the axis cannot scroll.</summary>
    Disabled,

    /// <summary>Shown only when the content is larger than the viewport.</summary>
    Auto,

    /// <summary>Not shown, but the axis can still scroll by wheel and keyboard.</summary>
    Hidden,

    /// <summary>Always shown.</summary>
    Visible,
}

/// <summary>Carries the offset an external-scroll host should move its viewport to.</summary>
public sealed class ScrollOffsetRequestedEventArgs(float horizontalOffset, float verticalOffset) : EventArgs
{
    /// <summary>The requested horizontal offset, in surface pixels.</summary>
    public float HorizontalOffset { get; } = horizontalOffset;

    /// <summary>The requested vertical offset, in surface pixels.</summary>
    public float VerticalOffset { get; } = verticalOffset;
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
            _child?.Dispose();
            _child = null;
        }

        base.Dispose(disposing);
    }
}
