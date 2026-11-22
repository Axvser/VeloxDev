using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>The axis a <see cref="ScrollBar"/> runs along.</summary>
public enum ScrollBarOrientation
{
    /// <summary>The bar runs left to right.</summary>
    Horizontal = 0,

    /// <summary>The bar runs top to bottom.</summary>
    Vertical = 1,
}

/// <summary>
/// A self-drawn scroll bar with a rounded track and a draggable thumb whose colours and thickness come from
/// dependency properties.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is delegated to the native scrollbar theme: the track and thumb are painted into the control's own
/// double buffer, so a dark XAML surface can restyle them with setters on <see cref="TrackBrush"/>,
/// <see cref="ThumbBrush"/>, <see cref="ThumbHoverBrush"/>, <see cref="ThumbBorderBrush"/>,
/// <see cref="ThumbBorderThickness"/>, <see cref="ThumbCornerRadius"/> and <see cref="BarThickness"/>.
/// </para>
/// <para>
/// The value model matches the native WinForms bar: <see cref="Value"/> is clamped to
/// <c>Minimum..(Maximum - LargeChange + 1)</c>, so a host can set <see cref="Maximum"/> to
/// <c>range + viewport - 1</c> and let the thumb travel exactly across the scrollable range. Clicking the track
/// pages by <see cref="LargeChange"/>; dragging the thumb updates <see cref="Value"/> continuously.
/// </para>
/// </remarks>
public class ScrollBar : Control
{
    /// <summary>The axis the bar runs along; <see cref="ScrollBarOrientation.Horizontal"/> by default.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(ScrollBarOrientation),
        typeof(ScrollBar),
        new PropertyMetadata(ScrollBarOrientation.Horizontal, OnVisualPropertyChanged));

    /// <summary>The lowest value the bar can report; zero by default.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(int),
        typeof(ScrollBar),
        new PropertyMetadata(0, OnRangePropertyChanged));

    /// <summary>The value at the far end of the track, in native scrollbar terms; 100 by default.</summary>
    /// <remarks>The largest selectable <see cref="Value"/> is <c>Maximum - LargeChange + 1</c>.</remarks>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(int),
        typeof(ScrollBar),
        new PropertyMetadata(100, OnRangePropertyChanged));

    /// <summary>The current position along the track; zero by default.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(ScrollBar),
        new PropertyMetadata(0, OnValuePropertyChanged, CoerceValue));

    /// <summary>The distance one track click or page moves; 10 by default.</summary>
    public static readonly DependencyProperty LargeChangeProperty = DependencyProperty.Register(
        nameof(LargeChange),
        typeof(int),
        typeof(ScrollBar),
        new PropertyMetadata(10, OnRangePropertyChanged));

    /// <summary>The distance one line moves; 1 by default.</summary>
    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange),
        typeof(int),
        typeof(ScrollBar),
        new PropertyMetadata(1));

    /// <summary>The brush painted behind the thumb; a 22/255 white by default.</summary>
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(ScrollBar),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), OnVisualPropertyChanged));

    /// <summary>The brush of the resting thumb; a 66/255 white by default.</summary>
    public static readonly DependencyProperty ThumbBrushProperty = DependencyProperty.Register(
        nameof(ThumbBrush),
        typeof(Brush),
        typeof(ScrollBar),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), OnVisualPropertyChanged));

    /// <summary>The brush of the thumb while the pointer is over it or it is being dragged; a 170/255 white by default.</summary>
    public static readonly DependencyProperty ThumbHoverBrushProperty = DependencyProperty.Register(
        nameof(ThumbHoverBrush),
        typeof(Brush),
        typeof(ScrollBar),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), OnVisualPropertyChanged));

    /// <summary>The brush of the thumb outline; transparent by default.</summary>
    public static readonly DependencyProperty ThumbBorderBrushProperty = DependencyProperty.Register(
        nameof(ThumbBorderBrush),
        typeof(Brush),
        typeof(ScrollBar),
        new PropertyMetadata(new SolidColorBrush(Color.Transparent), OnVisualPropertyChanged));

    /// <summary>The width of the thumb outline per side; an empty thickness draws no outline.</summary>
    public static readonly DependencyProperty ThumbBorderThicknessProperty = DependencyProperty.Register(
        nameof(ThumbBorderThickness),
        typeof(Thickness),
        typeof(ScrollBar),
        new PropertyMetadata(Thickness.Empty, OnVisualPropertyChanged));

    /// <summary>The thumb and track corner radius in pixels; 6 by default.</summary>
    public static readonly DependencyProperty ThumbCornerRadiusProperty = DependencyProperty.Register(
        nameof(ThumbCornerRadius),
        typeof(float),
        typeof(ScrollBar),
        new PropertyMetadata(6f, OnVisualPropertyChanged));

    /// <summary>The painted thickness of the bar, in pixels; 12 by default.</summary>
    public static readonly DependencyProperty BarThicknessProperty = DependencyProperty.Register(
        nameof(BarThickness),
        typeof(int),
        typeof(ScrollBar),
        new PropertyMetadata(12, OnVisualPropertyChanged));

    /// <summary>The shortest length the thumb may shrink to, in pixels; 24 by default.</summary>
    public static readonly DependencyProperty ThumbMinLengthProperty = DependencyProperty.Register(
        nameof(ThumbMinLength),
        typeof(int),
        typeof(ScrollBar),
        new PropertyMetadata(24, OnVisualPropertyChanged));

    private bool _dragging;
    private bool _hoverThumb;
    private int _dragStartAxis;
    private int _dragStartValue;

    /// <summary>Creates the bar with the double-buffered, self-painted style it needs.</summary>
    public ScrollBar()
    {
        // 轨道带 alpha，背景必须透明才会与父窗口的暗色表面合成；系统滚动条主题完全不参与。
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        Background = null;
        TabStop = false;
    }

    /// <summary>Raised after <see cref="Value"/> changes for any reason, including a drag or a clamping write.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Gets or sets the axis the bar runs along.</summary>
    public ScrollBarOrientation Orientation
    {
        get => (ScrollBarOrientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets the lowest value the bar can report.</summary>
    public int Minimum
    {
        get => (int)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the value at the far end of the track in native scrollbar terms.</summary>
    public int Maximum
    {
        get => (int)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the current position along the track.</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Gets or sets the distance one track click or page moves.</summary>
    public int LargeChange
    {
        get => (int)GetValue(LargeChangeProperty)!;
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>Gets or sets the distance one line moves.</summary>
    public int SmallChange
    {
        get => (int)GetValue(SmallChangeProperty)!;
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>Gets or sets the brush painted behind the thumb.</summary>
    public Brush? TrackBrush
    {
        get => GetValue(TrackBrushProperty) as Brush;
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Gets or sets the brush of the resting thumb.</summary>
    public Brush? ThumbBrush
    {
        get => GetValue(ThumbBrushProperty) as Brush;
        set => SetValue(ThumbBrushProperty, value);
    }

    /// <summary>Gets or sets the brush of the thumb while the pointer is over it or it is being dragged.</summary>
    public Brush? ThumbHoverBrush
    {
        get => GetValue(ThumbHoverBrushProperty) as Brush;
        set => SetValue(ThumbHoverBrushProperty, value);
    }

    /// <summary>Gets or sets the brush of the thumb outline.</summary>
    public Brush? ThumbBorderBrush
    {
        get => GetValue(ThumbBorderBrushProperty) as Brush;
        set => SetValue(ThumbBorderBrushProperty, value);
    }

    /// <summary>Gets or sets the width of the thumb outline per side.</summary>
    public Thickness ThumbBorderThickness
    {
        get => (Thickness)GetValue(ThumbBorderThicknessProperty)!;
        set => SetValue(ThumbBorderThicknessProperty, value);
    }

    /// <summary>Gets or sets the thumb and track corner radius in pixels.</summary>
    public float ThumbCornerRadius
    {
        get => (float)GetValue(ThumbCornerRadiusProperty)!;
        set => SetValue(ThumbCornerRadiusProperty, value);
    }

    /// <summary>Gets or sets the painted thickness of the bar in pixels.</summary>
    public int BarThickness
    {
        get => (int)GetValue(BarThicknessProperty)!;
        set => SetValue(BarThicknessProperty, value);
    }

    /// <summary>Gets or sets the shortest length the thumb may shrink to, in pixels.</summary>
    public int ThumbMinLength
    {
        get => (int)GetValue(ThumbMinLengthProperty)!;
        set => SetValue(ThumbMinLengthProperty, value);
    }

    /// <summary>The largest <see cref="Value"/> the current range allows.</summary>
    private int MaximumValue => Math.Max(Minimum, Maximum - Math.Max(1, LargeChange) + 1);

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = GetTrackBounds();
        if (track.Width <= 0 || track.Height <= 0)
        {
            return;
        }

        var trackRadius = Math.Min(ThumbCornerRadius, Math.Min(track.Width, track.Height) / 2f);
        var trackBrushValue = TrackBrush;
        using (var trackPath = CreateRoundedPath(track, trackRadius))
        {
            if (trackBrushValue is { } trackBrush && trackBrush.ApproximateColor.A > 0)
            {
                trackBrush.FillPath(graphics, trackPath);
            }
        }

        var thumb = GetThumbBounds(track);
        if (thumb.Width <= 0 || thumb.Height <= 0)
        {
            return;
        }

        var thumbRadius = Math.Min(ThumbCornerRadius, Math.Min(thumb.Width, thumb.Height) / 2f);
        using (var thumbPath = CreateRoundedPath(thumb, thumbRadius))
        {
            var thumbBrushValue = _dragging || _hoverThumb ? ThumbHoverBrush : ThumbBrush;
            if (thumbBrushValue is { } thumbBrush && thumbBrush.ApproximateColor.A > 0)
            {
                thumbBrush.FillPath(graphics, thumbPath);
            }

            if (ThumbBorderBrush is { } borderBrush && borderBrush.ApproximateColor.A > 0 && !ThumbBorderThickness.IsEmpty)
            {
                using var ring = CreateBorderRing(thumb, thumbRadius, ThumbBorderThickness);
                if (ring is not null)
                {
                    borderBrush.FillPath(graphics, ring);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var track = GetTrackBounds();
        if (track.Contains(e.Location) && !GetThumbBounds(track).Contains(e.Location))
        {
            Page(e.Location, track);
            return;
        }

        var thumb = GetThumbBounds(track);
        if (!thumb.Contains(e.Location))
        {
            return;
        }

        _dragging = true;
        _dragStartAxis = AxisOf(e.Location);
        _dragStartValue = Value;
        CapturePointer();
        if (!_hoverThumb)
        {
            _hoverThumb = true;
        }

        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            var track = GetTrackBounds();
            var thumb = GetThumbBounds(track);
            var trackLength = Orientation == ScrollBarOrientation.Horizontal ? track.Width : track.Height;
            var thumbLength = Orientation == ScrollBarOrientation.Horizontal ? thumb.Width : thumb.Height;
            var travel = Math.Max(0, trackLength - thumbLength);
            var amplitude = MaximumValue - Minimum;

            if (travel > 0 && amplitude > 0)
            {
                var delta = AxisOf(e.Location) - _dragStartAxis;
                Value = (int)Math.Round(_dragStartValue + (double)delta * amplitude / travel);
            }

            return;
        }

        UpdateHover(e.Location);
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
        UpdateHover(e.Location);
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverThumb)
        {
            _hoverThumb = false;
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_dragging && !Capture)
        {
            _dragging = false;
            Invalidate();
        }
    }

    private void UpdateHover(Point location)
    {
        var hover = GetThumbBounds(GetTrackBounds()).Contains(location);
        if (hover == _hoverThumb)
        {
            return;
        }

        _hoverThumb = hover;
        Invalidate();
    }

    private void Page(Point location, Rectangle track)
    {
        var thumb = GetThumbBounds(track);
        if (thumb.Width <= 0 || thumb.Height <= 0)
        {
            return;
        }

        var step = Math.Max(1, LargeChange);
        Value = AxisOf(location) < AxisOf(thumb.Location) ? Value - step : Value + step;
    }

    private int AxisOf(Point point)
        => Orientation == ScrollBarOrientation.Horizontal ? point.X : point.Y;

    private Rectangle GetTrackBounds()
    {
        var thickness = Math.Max(1, BarThickness);
        if (Orientation == ScrollBarOrientation.Horizontal)
        {
            var height = Math.Min(Math.Max(0, ClientSize.Height), thickness);
            return new Rectangle(0, Math.Max(0, (ClientSize.Height - height) / 2), Math.Max(0, ClientSize.Width), height);
        }

        var width = Math.Min(Math.Max(0, ClientSize.Width), thickness);
        return new Rectangle(Math.Max(0, (ClientSize.Width - width) / 2), 0, width, Math.Max(0, ClientSize.Height));
    }

    private Rectangle GetThumbBounds(Rectangle track)
    {
        if (track.Width <= 0 || track.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var horizontal = Orientation == ScrollBarOrientation.Horizontal;
        var trackLength = horizontal ? track.Width : track.Height;
        var span = Maximum - Minimum + 1;
        var largeChange = Math.Max(1, LargeChange);
        var ratio = span > 0 ? (double)largeChange / span : 1d;
        var thumbLength = (int)Math.Round(trackLength * ratio);
        var minimumLength = Math.Min(Math.Max(1, ThumbMinLength), trackLength);
        thumbLength = Clamp(thumbLength, minimumLength, trackLength);

        var travel = trackLength - thumbLength;
        var amplitude = MaximumValue - Minimum;
        var position = amplitude <= 0 ? 0d : (double)(Value - Minimum) / amplitude * travel;
        var offset = (int)Math.Round(Clamp(position, 0d, travel));

        return horizontal
            ? new Rectangle(track.X + offset, track.Y, thumbLength, track.Height)
            : new Rectangle(track.X, track.Y + offset, track.Width, thumbLength);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var clamped = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        var diameter = 2f * clamped;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    // 四边宽度不等时单支笔画不出 WPF 的 BorderThickness，按「外轮廓挖内轮廓」填环。
    private static GraphicsPath? CreateBorderRing(Rectangle bounds, float radius, Thickness thickness)
    {
        var left = Math.Max(0f, thickness.Left);
        var top = Math.Max(0f, thickness.Top);
        var right = Math.Max(0f, thickness.Right);
        var bottom = Math.Max(0f, thickness.Bottom);

        var outer = CreateRoundedPath(bounds, radius);
        var inner = new RectangleF(
            bounds.X + left,
            bounds.Y + top,
            bounds.Width - left - right,
            bounds.Height - top - bottom);
        if (inner.Width <= 0f || inner.Height <= 0f)
        {
            return outer;
        }

        var inset = Math.Max(Math.Max(left, right), Math.Max(top, bottom));
        using var innerPath = CreateRoundedPath(
            Rectangle.Round(inner),
            Math.Max(0f, radius - inset));

        outer.FillMode = FillMode.Alternate;
        outer.AddPath(innerPath, connect: false);
        return outer;
    }

    private static object? CoerceValue(System.Windows.Forms.Control target, object? baseValue)
    {
        if (target is not ScrollBar bar || baseValue is not int value)
        {
            return baseValue;
        }

        return Clamp(value, bar.Minimum, bar.MaximumValue);
    }

    private static void OnValuePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ScrollBar bar || bar.IsDisposed)
        {
            return;
        }

        bar.ValueChanged?.Invoke(bar, EventArgs.Empty);
        bar.Invalidate();
    }

    private static void OnRangePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ScrollBar bar || bar.IsDisposed)
        {
            return;
        }

        // 范围收窄后旧 Value 可能落在界外；写回会触发 ValueChanged，订阅方本来也需要这次通知。
        var clamped = Clamp(bar.Value, bar.Minimum, bar.MaximumValue);
        if (clamped != bar.Value)
        {
            bar.SetValue(ValueProperty, clamped);
        }

        bar.Invalidate();
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ScrollBar bar && !bar.IsDisposed)
        {
            bar.Invalidate();
        }
    }
}
