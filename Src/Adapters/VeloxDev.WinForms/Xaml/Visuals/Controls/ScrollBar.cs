using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A single-axis scroll bar with a draggable thumb, an auto-repeating track and keyboard paging.</summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is clamped to <see cref="Minimum"/> through <see cref="Maximum"/> minus
/// <see cref="ViewportSize"/>; when <see cref="ViewportSize"/> is zero the whole <see cref="Maximum"/> is scrollable.
/// That is the viewport model: <see cref="Maximum"/> is the extent and <see cref="ViewportSize"/> is its visible part.
/// </para>
/// <para>
/// Without a <see cref="Template"/> the bar paints a rounded thumb in the current theme. A template is applied with
/// the same name scope and templated-parent rules as <see cref="ContentControl"/>; the optional <c>PART_Track</c>,
/// <c>PART_Thumb</c>, <c>PART_DecreaseRepeatButton</c> and <c>PART_IncreaseRepeatButton</c> parts replace the matching
/// hit-test and painting regions. A template that declares no parts falls back to the default thumb.
/// </para>
/// <para>
/// A press on a repeat-button part, or on the track outside the thumb, pages once and then keeps paging while the
/// pointer stays down; the repeat stops at the bound, on pointer release or exit, and when the bar is disabled or
/// its template changes.
/// </para>
/// </remarks>
public class ScrollBar : VisualElement
{
    private const float BarThickness = 6f;
    private const float BarInset = 2f;
    private const float BarHitPadding = 3f;
    private const float MinThumbLength = 24f;

    // 与 WPF 的 RepeatButton 同一节奏：按住先等一拍再连发，轻点不会变成连滚。
    private const int RepeatStartDelayMilliseconds = 400;
    private const int RepeatIntervalMilliseconds = 50;

    /// <summary>Whether the bar runs vertically (default) or horizontally.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(ScrollBar),
        new PropertyMetadata(Orientation.Vertical) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The lowest value.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ScrollBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The full extent of the scrolled content.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ScrollBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The current scroll offset, clamped into the scrollable range.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ScrollBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The visible length of the extent, which sizes the thumb and shortens the scrollable range.</summary>
    public static readonly DependencyProperty ViewportSizeProperty = DependencyProperty.Register(
        nameof(ViewportSize), typeof(double), typeof(ScrollBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The distance an arrow key or a small step moves <see cref="Value"/>.</summary>
    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(double), typeof(ScrollBar),
        new PropertyMetadata(16d));

    /// <summary>The distance a track press or a page key moves <see cref="Value"/>.</summary>
    public static readonly DependencyProperty LargeChangeProperty = DependencyProperty.Register(
        nameof(LargeChange), typeof(double), typeof(ScrollBar),
        new PropertyMetadata(48d));

    /// <summary>A template applied directly to this bar, or <see langword="null"/> to use the style's template.</summary>
    public static readonly DependencyProperty TemplateProperty = DependencyProperty.Register(
        nameof(Template), typeof(VisualControlTemplate), typeof(ScrollBar),
        new PropertyMetadata(null) { AffectsMeasure = true });

    /// <summary>How much of the far end of the track a crossing scroll bar occupies.</summary>
    /// <remarks>
    /// A host that lays the two bars out in overlapping strips sets this to the cross bar's thickness plus inset so
    /// the track stops before the corner. A host that gives each bar a non-overlapping slot leaves it at zero.
    /// </remarks>
    public static readonly DependencyProperty ReserveCornerSizeProperty = DependencyProperty.Register(
        nameof(ReserveCornerSize), typeof(double), typeof(ScrollBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    private VisualControlTemplate? _styleTemplate;
    private VisualControlTemplate? _appliedTemplate;
    private VisualElement? _templateRoot;
    private VisualTemplateTriggerApplication? _templateTriggers;
    private bool _dragging;
    private bool _hover;
    private float _dragStartAxis;
    private double _dragStartValue;
    private System.Windows.Forms.Timer? _repeatTimer;
    private bool _repeatIncreases;

    /// <summary>Creates a focusable vertical bar.</summary>
    public ScrollBar()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = XamlThickness.Empty;

        // 预览先于目标部件自己的按下处理；模板里的重复按钮即使是 Button，也拿不到这一笔。
        PreviewPointerPressed += OnPreviewPointerPressed;
    }

    /// <summary>Raised after <see cref="Value"/> changes, whether from user input, a template or a binding.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised when user input asks for a new offset.</summary>
    public event EventHandler<ScrollBarScrollEventArgs>? Scroll;

    /// <summary>Whether the bar runs vertically (default) or horizontally.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>The lowest value.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The full extent of the scrolled content.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The current scroll offset, clamped into the scrollable range.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, ClampValue(value));
    }

    /// <summary>The visible length of the extent, which sizes the thumb and shortens the scrollable range.</summary>
    public double ViewportSize
    {
        get => (double)GetValue(ViewportSizeProperty)!;
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>The distance an arrow key or a small step moves <see cref="Value"/>.</summary>
    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty)!;
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>The distance a track press or a page key moves <see cref="Value"/>.</summary>
    public double LargeChange
    {
        get => (double)GetValue(LargeChangeProperty)!;
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>A template applied directly to this bar, or <see langword="null"/> to use the style's template.</summary>
    public VisualControlTemplate? Template
    {
        get => (VisualControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>How much of the far end of the track a crossing scroll bar occupies.</summary>
    public double ReserveCornerSize
    {
        get => (double)GetValue(ReserveCornerSizeProperty)!;
        set => SetValue(ReserveCornerSizeProperty, value);
    }

    /// <summary>The root of the applied template, or <see langword="null"/> when none is applied.</summary>
    public VisualElement? TemplateRoot => _templateRoot;

    /// <summary>Returns whether <paramref name="localPoint"/> lies on this bar's track.</summary>
    /// <param name="localPoint">A point in this bar's local coordinates.</param>
    public bool IsOverTrack(PointF localPoint) => HitTestContent(localPoint);

    // 样式带 Template 时由 VisualStyleApplication 回调；与 ContentControl 走同一套应用流程。
    internal override void ApplyStyleTemplate(VisualControlTemplate? template)
    {
        _styleTemplate = template;
        UpdateTemplate();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var innerWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal));
        var innerHeight = Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical));

        // 模板子树必须先量，否则里面的面板拿不到子项尺寸；外层仍按默认条宽排版。
        var templated = _templateRoot?.Measure(new Size(innerWidth, innerHeight)) ?? Size.Empty;

        var defaultCross = (int)Math.Ceiling(BarThickness + BarInset);
        var mainAvailable = Orientation == Orientation.Vertical ? innerHeight : innerWidth;
        var main = mainAvailable > 0 && mainAvailable < int.MaxValue / 2 ? mainAvailable : 120;

        if (Orientation == Orientation.Vertical)
        {
            return new Size(
                Math.Max(defaultCross, templated.Width) + (int)Math.Ceiling(padding.Horizontal),
                main + (int)Math.Ceiling(padding.Vertical));
        }

        return new Size(
            main + (int)Math.Ceiling(padding.Horizontal),
            Math.Max(defaultCross, templated.Height) + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (_templateRoot is null)
        {
            return;
        }

        var padding = Padding;
        _templateRoot.Arrange(new RectangleF(
            (int)Math.Ceiling(padding.Left),
            (int)Math.Ceiling(padding.Top),
            Math.Max(0f, finalSize.Width - (float)padding.Horizontal),
            Math.Max(0f, finalSize.Height - (float)padding.Vertical)));

        if (GetTemplateChild<VisualElement>("PART_Thumb") is { } thumb)
        {
            // 拇指由滚动条摆放，不受模板自己的面板排版支配；换算到它的直接父级坐标再 Arrange。
            var origin = ParentOrigin(thumb);
            var thumbRect = GetThumbRect(GetTrackRect());
            thumb.Arrange(new RectangleF(
                thumbRect.X - origin.X,
                thumbRect.Y - origin.Y,
                thumbRect.Width,
                thumbRect.Height));
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, TemplateProperty))
        {
            UpdateTemplate();
        }
        else if (ReferenceEquals(e.Property, ValueProperty))
        {
            // 绑定或模板写入绕过 CLR setter，这里再裁一次；只有生效值真的变了才发事件。
            var clamped = ClampValue(Value);
            if (clamped != Value)
            {
                SetValue(ValueProperty, clamped);
                return;
            }

            ValueChanged?.Invoke(this, EventArgs.Empty);

            // 默认拇指是画出来的，重画自身条带即可；模板拇指是子元素，需要重排才能跟着 Value 走。
            if (HasTemplatedThumb)
            {
                InvalidateArrange();
            }
        }
        else if (ReferenceEquals(e.Property, MinimumProperty)
                 || ReferenceEquals(e.Property, MaximumProperty)
                 || ReferenceEquals(e.Property, ViewportSizeProperty))
        {
            CoerceValue();
            if (HasTemplatedThumb)
            {
                InvalidateArrange();
            }
        }
        else if (ReferenceEquals(e.Property, IsEnabledProperty) && !IsEnabled)
        {
            // 禁用时立即收手，不等下一次 tick。
            StopRepeat();
        }
    }

    /// <inheritdoc />
    protected override void RenderChildren(Graphics graphics)
    {
        base.RenderChildren(graphics);
        DrawDefaultThumb(graphics);
    }

    /// <inheritdoc />
    protected override bool HitTestContent(PointF point)
        => IsHitTestVisible && Inflate(GetTrackRect()).Contains(point);

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
        => UpdateHover(args.GetPosition(this));

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        // 指针离开条带（即使仍被捕获）就不再连发。
        StopRepeat();
        if (!_dragging)
        {
            SetHover(false);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        var point = args.GetPosition(this);
        Focus();
        UpdateHover(point);

        // 轨道与重复按钮已由预览阶段接管，能走到这里的只剩拇指。
        if (!Inflate(GetThumbRect(GetTrackRect())).Contains(point))
        {
            return;
        }

        BeginDrag(point);
        args.Handled = true;
    }

    // 预览比模板部件自己的 OnPointerPressed 先到，在这里接管翻页，部件就不会再处理同一笔。
    private void OnPreviewPointerPressed(object? sender, VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        var point = args.GetPosition(this);
        var thumb = GetThumbRect(GetTrackRect());

        // 拇指按下仍走 OnPointerPressed 的拖动路径。
        if (Inflate(thumb).Contains(point))
        {
            return;
        }

        if (HitRepeatButton("PART_DecreaseRepeatButton", point))
        {
            BeginPagePress(point, increase: false);
            args.Handled = true;
            return;
        }

        if (HitRepeatButton("PART_IncreaseRepeatButton", point))
        {
            BeginPagePress(point, increase: true);
            args.Handled = true;
            return;
        }

        if (!Inflate(GetTrackRect()).Contains(point))
        {
            return;
        }

        // 默认轨道按点击位置落在拇指起点的哪一侧决定方向。
        BeginPagePress(point, increase: AxisOf(point) >= StartOf(thumb));
        args.Handled = true;
    }

    // 翻一页并保留拇指拖动；只有值真的移动才启动连发。
    private void BeginPagePress(PointF point, bool increase)
    {
        Focus();
        UpdateHover(point);
        var moved = PageOnce(increase);
        BeginDrag(point);
        if (moved)
        {
            StartRepeat(increase);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        var point = args.GetPosition(this);
        UpdateHover(point);

        if (!_dragging)
        {
            return;
        }

        var track = GetTrackRect();
        var thumb = GetThumbRect(track);
        var travel = LengthOf(track) - LengthOf(thumb);
        var range = EffectiveMaximum - EffectiveMinimum;
        if (travel > 0.5f && range > 0d)
        {
            var delta = AxisOf(point) - _dragStartAxis;
            ScrollTo(_dragStartValue + (delta * range / travel), ScrollBarScrollEventType.ThumbTrack);
        }

        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        // 松开就停连发，不留活着的定时器。
        StopRepeat();

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        UpdateHover(args.GetPosition(this));
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        switch (args.KeyCode)
        {
            case Keys.PageUp:
                ScrollTo(Value - PageStep, ScrollBarScrollEventType.LargeDecrement);
                args.Handled = true;
                break;
            case Keys.PageDown:
                ScrollTo(Value + PageStep, ScrollBarScrollEventType.LargeIncrement);
                args.Handled = true;
                break;
            case Keys.Home:
                ScrollTo(EffectiveMinimum, ScrollBarScrollEventType.First);
                args.Handled = true;
                break;
            case Keys.End:
                ScrollTo(EffectiveMaximum, ScrollBarScrollEventType.Last);
                args.Handled = true;
                break;
            case Keys.Up:
            case Keys.Left:
                ScrollTo(Value - SmallStep, ScrollBarScrollEventType.SmallDecrement);
                args.Handled = true;
                break;
            case Keys.Down:
            case Keys.Right:
                ScrollTo(Value + SmallStep, ScrollBarScrollEventType.SmallIncrement);
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopRepeat();
            RemoveTemplate();
        }

        base.Dispose(disposing);
    }

    // 默认拇指画在子项之上：模板只提供了轨道、没提供拇指时，回退的拇指不能被模板根盖住。
    private void DrawDefaultThumb(Graphics graphics)
    {
        if (HasTemplatedThumb)
        {
            return;
        }

        var thumb = GetThumbRect(GetTrackRect());
        if (thumb.Width <= 0f || thumb.Height <= 0f)
        {
            return;
        }

        var theme = Theme.Current;
        using var brush = new SolidBrush(ThumbColor(theme, _dragging || _hover));
        using var path = XamlDrawing.RoundedRectangle(thumb, 3f);
        graphics.FillPath(brush, path);
    }

    private static Color ThumbColor(ThemePalette theme, bool highlighted)
        => highlighted
            ? Color.FromArgb(0xCC, theme.Foreground)
            : Color.FromArgb(0x66, theme.MutedForeground);

    private bool HasTemplatedThumb
        => _templateRoot is not null && GetTemplateChild<VisualElement>("PART_Thumb") is not null;

    private bool HitRepeatButton(string name, PointF point)
    {
        if (GetTemplateChild<VisualElement>(name) is not { } button)
        {
            return false;
        }

        return Inflate(ToScrollBarCoordinates(button)).Contains(point);
    }

    private RectangleF GetTrackRect()
    {
        if (_templateRoot is not null && GetTemplateChild<VisualElement>("PART_Track") is { } track)
        {
            var local = ToScrollBarCoordinates(track);
            if (local.Width > 0f && local.Height > 0f)
            {
                return local;
            }
        }

        return GetDefaultTrackRect();
    }

    private RectangleF GetDefaultTrackRect()
    {
        var padding = Padding;
        var width = Math.Max(0f, Bounds.Width);
        var height = Math.Max(0f, Bounds.Height);

        if (Orientation == Orientation.Vertical)
        {
            var thickness = Math.Min(BarThickness, Math.Max(0f, width - (float)padding.Horizontal - BarInset));
            var x = Math.Max(0f, width - (float)padding.Right - BarInset - thickness);
            var length = Math.Max(0f, height - (float)padding.Vertical - (float)ReserveCornerSize);
            return new RectangleF(x, (float)padding.Top, thickness, length);
        }

        var crossThickness = Math.Min(BarThickness, Math.Max(0f, height - (float)padding.Vertical - BarInset));
        var y = Math.Max(0f, height - (float)padding.Bottom - BarInset - crossThickness);
        var mainLength = Math.Max(0f, width - (float)padding.Horizontal - (float)ReserveCornerSize);
        return new RectangleF((float)padding.Left, y, mainLength, crossThickness);
    }

    private RectangleF GetThumbRect(RectangleF track)
    {
        var trackLength = LengthOf(track);
        if (trackLength <= 0f)
        {
            return RectangleF.Empty;
        }

        var thumbLength = Math.Min(trackLength, Math.Max(MinThumbLength, trackLength * ThumbRatio()));
        var range = EffectiveMaximum - EffectiveMinimum;
        var fraction = range <= 0d ? 0d : XamlMath.Clamp((Value - EffectiveMinimum) / range, 0d, 1d);
        var travel = Math.Max(0f, trackLength - thumbLength);
        var start = StartOf(track) + (float)(fraction * travel);

        return Orientation == Orientation.Vertical
            ? new RectangleF(track.X, start, track.Width, thumbLength)
            : new RectangleF(start, track.Y, thumbLength, track.Height);
    }

    // 拇指长度取「视口 / 总范围」；视口未知（0）时按整条轨道画，表示没有可滚动的余量。
    private float ThumbRatio()
    {
        var extent = Math.Max(0d, EffectiveExtentMaximum - EffectiveMinimum);
        if (extent <= 0d)
        {
            return 1f;
        }

        var viewport = XamlMath.IsFinite(ViewportSize) ? Math.Max(0d, ViewportSize) : 0d;
        return viewport <= 0d ? 1f : (float)XamlMath.Clamp(viewport / extent, 0d, 1d);
    }

    private double EffectiveMinimum
        => XamlMath.IsFinite(Minimum) ? Minimum : 0d;

    private double EffectiveExtentMaximum
        => XamlMath.IsFinite(Maximum) ? Math.Max(EffectiveMinimum, Maximum) : EffectiveMinimum;

    private double EffectiveMaximum
    {
        get
        {
            var extentMaximum = EffectiveExtentMaximum;
            var viewport = XamlMath.IsFinite(ViewportSize) ? Math.Max(0d, ViewportSize) : 0d;
            return viewport > 0d ? Math.Max(EffectiveMinimum, extentMaximum - viewport) : extentMaximum;
        }
    }

    private double PageStep
        => LargeChange > 0d ? LargeChange : ViewportSize > 0d ? ViewportSize : 1d;

    private double SmallStep
        => SmallChange > 0d ? SmallChange : 1d;

    private double ClampValue(double value)
        => XamlMath.IsFinite(value)
            ? XamlMath.Clamp(value, EffectiveMinimum, EffectiveMaximum)
            : EffectiveMinimum;

    private void CoerceValue()
    {
        var clamped = ClampValue(Value);
        if (clamped == Value)
        {
            return;
        }

        SetValue(ValueProperty, clamped);
    }

    private void ScrollTo(double value, ScrollBarScrollEventType eventType)
    {
        var clamped = ClampValue(value);
        if (clamped == Value)
        {
            return;
        }

        SetValue(ValueProperty, clamped);
        Scroll?.Invoke(this, new ScrollBarScrollEventArgs(clamped, eventType));
    }

    private void BeginDrag(PointF point)
    {
        _dragging = true;
        _dragStartAxis = AxisOf(point);
        _dragStartValue = Value;
        InvalidateVisual();
    }

    // 翻一页并报告是否真的移动；停在边界时 ScrollTo 是空操作。
    private bool PageOnce(bool increase)
    {
        var before = Value;
        ScrollTo(
            Value + (increase ? PageStep : -PageStep),
            increase ? ScrollBarScrollEventType.LargeIncrement : ScrollBarScrollEventType.LargeDecrement);
        return Value != before;
    }

    private void StartRepeat(bool increase)
    {
        StopRepeat();
        _repeatIncreases = increase;
        _repeatTimer = new System.Windows.Forms.Timer { Interval = RepeatStartDelayMilliseconds };
        _repeatTimer.Tick += OnRepeatTick;
        _repeatTimer.Start();
    }

    private void OnRepeatTick(object? sender, EventArgs e)
    {
        // 首次 tick 已过初始延迟，此后按连发间隔。
        if (_repeatTimer is { } timer)
        {
            timer.Interval = RepeatIntervalMilliseconds;
        }

        RepeatOnce();
    }

    // 测试用：立刻执行一次连发翻页，免去等待初始延迟。
    internal bool RepeatPageForTest() => RepeatOnce();

    // 连发是否仍在运行；测试据此确认释放、禁用或换模板后定时器已经停掉。
    internal bool IsRepeatActive => _repeatTimer is not null;

    private bool RepeatOnce()
    {
        if (_repeatTimer is null)
        {
            return false;
        }

        if (!IsEnabled)
        {
            StopRepeat();
            return false;
        }

        if (!PageOnce(_repeatIncreases))
        {
            // 已到边界：继续按住也产生不了新值。
            StopRepeat();
            return false;
        }

        return true;
    }

    private void StopRepeat()
    {
        if (_repeatTimer is not { } timer)
        {
            return;
        }

        timer.Tick -= OnRepeatTick;
        timer.Stop();
        timer.Dispose();
        _repeatTimer = null;
    }

    private void UpdateHover(PointF point) => SetHover(Inflate(GetTrackRect()).Contains(point));

    private void SetHover(bool hover)
    {
        if (_hover == hover)
        {
            return;
        }

        _hover = hover;
        InvalidateVisual();
    }

    private float AxisOf(PointF point)
        => Orientation == Orientation.Vertical ? point.Y : point.X;

    private float StartOf(RectangleF rect)
        => Orientation == Orientation.Vertical ? rect.Top : rect.Left;

    private float LengthOf(RectangleF rect)
        => Orientation == Orientation.Vertical ? rect.Height : rect.Width;

    private static RectangleF Inflate(RectangleF rect)
    {
        rect.Inflate(BarHitPadding, BarHitPadding);
        return rect;
    }

    // 把某个后代的 Bounds 累加到本控件坐标系；模板把部件嵌在面板里时也能算出正确位置。
    private RectangleF ToScrollBarCoordinates(VisualElement element)
    {
        var x = element.Bounds.X;
        var y = element.Bounds.Y;
        for (var current = element.ParentElement; current is not null && !ReferenceEquals(current, this); current = current.ParentElement)
        {
            x += current.Bounds.X;
            y += current.Bounds.Y;
        }

        return new RectangleF(x, y, element.Bounds.Width, element.Bounds.Height);
    }

    private PointF ParentOrigin(VisualElement element)
    {
        var origin = PointF.Empty;
        for (var current = element.ParentElement; current is not null && !ReferenceEquals(current, this); current = current.ParentElement)
        {
            origin.X += current.Bounds.X;
            origin.Y += current.Bounds.Y;
        }

        return origin;
    }

    private void UpdateTemplate()
    {
        var effective = Template ?? _styleTemplate;
        if (ReferenceEquals(effective, _appliedTemplate))
        {
            return;
        }

        RemoveTemplate();
        _appliedTemplate = effective;
        if (effective is null)
        {
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        var instance = effective.CreateContent(this);
        _templateRoot = instance.Root;
        SetTemplateScope(instance.Scope);
        Add(instance.Root);
        _templateTriggers = new VisualTemplateTriggerApplication(this, instance.Scope, effective.Triggers);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RemoveTemplate()
    {
        // 换模板（含释放）时连发必须停，否则旧定时器仍会驱动本控件。
        StopRepeat();
        _templateTriggers?.Dispose();
        _templateTriggers = null;
        SetTemplateScope(null);

        if (_templateRoot is not null)
        {
            if (Children.Contains(_templateRoot))
            {
                RemoveChild(_templateRoot);
            }

            _templateRoot.Dispose();
            _templateRoot = null;
        }
    }
}

/// <summary>How a <see cref="ScrollBar"/> arrived at a new offset.</summary>
public enum ScrollBarScrollEventType
{
    /// <summary>An arrow key or another small step decreased the value.</summary>
    SmallDecrement,

    /// <summary>An arrow key or another small step increased the value.</summary>
    SmallIncrement,

    /// <summary>A track press or page key decreased the value.</summary>
    LargeDecrement,

    /// <summary>A track press or page key increased the value.</summary>
    LargeIncrement,

    /// <summary>The thumb was dragged.</summary>
    ThumbTrack,

    /// <summary>The value jumped to its first position.</summary>
    First,

    /// <summary>The value jumped to its last position.</summary>
    Last,
}

/// <summary>Carries the offset a user input asked a <see cref="ScrollBar"/> to show.</summary>
public sealed class ScrollBarScrollEventArgs : EventArgs
{
    internal ScrollBarScrollEventArgs(double newValue, ScrollBarScrollEventType scrollEventType)
    {
        NewValue = newValue;
        ScrollEventType = scrollEventType;
    }

    /// <summary>The requested offset after clamping.</summary>
    public double NewValue { get; }

    /// <summary>The gesture that produced the request.</summary>
    public ScrollBarScrollEventType ScrollEventType { get; }
}
