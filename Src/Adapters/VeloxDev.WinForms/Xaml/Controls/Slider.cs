using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A self-drawn slider with dependency-property range, orientation and colours.
/// </summary>
/// <remarks>
/// Dragging the thumb or clicking the track writes <see cref="Value"/> through the local-value path, so a two-way
/// binding sees the change. <see cref="IsSnapToTickEnabled"/> rounds the value to <see cref="TickFrequency"/>.
/// </remarks>
public class Slider : Control
{
    /// <summary>The lowest value; zero by default.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(Slider),
        new PropertyMetadata(0d, OnRangePropertyChanged));

    /// <summary>The highest value; 100 by default.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Slider),
        new PropertyMetadata(100d, OnRangePropertyChanged));

    /// <summary>The current value; zero by default.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(Slider),
        new PropertyMetadata(0d, OnValuePropertyChanged, CoerceValue));

    /// <summary>Whether the slider runs horizontally (the default) or vertically.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(Slider),
        new PropertyMetadata(Orientation.Horizontal, OnVisualPropertyChanged));

    /// <summary>The distance one keyboard step moves; one by default.</summary>
    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange),
        typeof(double),
        typeof(Slider),
        new PropertyMetadata(1d));

    /// <summary>The tick interval used when snapping; zero disables snapping.</summary>
    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(
        nameof(TickFrequency),
        typeof(double),
        typeof(Slider),
        new PropertyMetadata(0d));

    /// <summary>Whether the value is rounded to <see cref="TickFrequency"/>.</summary>
    public static readonly DependencyProperty IsSnapToTickEnabledProperty = DependencyProperty.Register(
        nameof(IsSnapToTickEnabled),
        typeof(bool),
        typeof(Slider),
        new PropertyMetadata(false));

    /// <summary>The brush of the thumb.</summary>
    public static readonly DependencyProperty ThumbBrushProperty = DependencyProperty.Register(
        nameof(ThumbBrush),
        typeof(Brush),
        typeof(Slider),
        new PropertyMetadata(new SolidColorBrush(Color.White), OnVisualPropertyChanged));

    /// <summary>The diameter of the thumb in pixels; 14 by default.</summary>
    public static readonly DependencyProperty ThumbSizeProperty = DependencyProperty.Register(
        nameof(ThumbSize),
        typeof(int),
        typeof(Slider),
        new PropertyMetadata(14, OnVisualPropertyChanged));

    private bool dragging;

    /// <summary>Gets or sets the lowest value.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the highest value.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the current value.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Gets or sets the slider orientation.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets the distance one keyboard step moves.</summary>
    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty)!;
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>Gets or sets the tick interval used when snapping; zero disables snapping.</summary>
    public double TickFrequency
    {
        get => (double)GetValue(TickFrequencyProperty)!;
        set => SetValue(TickFrequencyProperty, value);
    }

    /// <summary>Gets or sets whether the value is rounded to <see cref="TickFrequency"/>.</summary>
    public bool IsSnapToTickEnabled
    {
        get => (bool)GetValue(IsSnapToTickEnabledProperty)!;
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

    /// <summary>Gets or sets the brush of the thumb.</summary>
    public Brush? ThumbBrush
    {
        get => GetValue(ThumbBrushProperty) as Brush;
        set => SetValue(ThumbBrushProperty, value);
    }

    /// <summary>Gets or sets the diameter of the thumb in pixels.</summary>
    public int ThumbSize
    {
        get => (int)GetValue(ThumbSizeProperty)!;
        set => SetValue(ThumbSizeProperty, value);
    }

    /// <summary>Raised when <see cref="Value"/> changes.</summary>
    public event EventHandler? ValueChanged;

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
        => Orientation == Orientation.Horizontal
            ? new Size(proposedSize.Width > 0 ? proposedSize.Width : 160, Math.Max(24, ThumbSize + 8))
            : new Size(Math.Max(24, ThumbSize + 8), proposedSize.Height > 0 ? proposedSize.Height : 160);

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !IsEnabled)
        {
            return;
        }

        Focus();
        dragging = true;
        CapturePointer();
        SetValueFromPoint(e.Location);
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            SetValueFromPoint(e.Location);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragging = false;
        base.OnMouseUp(e);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled)
        {
            return;
        }

        var delta = e.KeyCode switch
        {
            Keys.Left or Keys.Down => -Math.Max(SmallChange, 0.0001d),
            Keys.Right or Keys.Up => Math.Max(SmallChange, 0.0001d),
            _ => 0d,
        };
        if (delta != 0d)
        {
            Value = Coerce(Value + delta);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = XamlLayout.ContentBounds(this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var thumb = Math.Max(4, ThumbSize);
        var fraction = (float)Fraction();
        var trackThickness = Math.Max(3, thumb / 3);

        if (Orientation == Orientation.Horizontal)
        {
            var track = new Rectangle(bounds.Left + thumb / 2, bounds.Top + ((bounds.Height - trackThickness) / 2), Math.Max(0, bounds.Width - thumb), trackThickness);
            PaintTrack(e.Graphics, track, fraction, horizontal: true);
            var x = track.Left + (int)Math.Round(track.Width * fraction) - (thumb / 2);
            PaintThumb(e.Graphics, new Rectangle(x, bounds.Top + ((bounds.Height - thumb) / 2), thumb, thumb));
        }
        else
        {
            var track = new Rectangle(bounds.Left + ((bounds.Width - trackThickness) / 2), bounds.Top + thumb / 2, trackThickness, Math.Max(0, bounds.Height - thumb));
            PaintTrack(e.Graphics, track, fraction, horizontal: false);
            var y = track.Bottom - (int)Math.Round(track.Height * fraction) - (thumb / 2);
            PaintThumb(e.Graphics, new Rectangle(bounds.Left + ((bounds.Width - thumb) / 2), y, thumb, thumb));
        }
    }

    private void PaintTrack(Graphics graphics, Rectangle track, float fraction, bool horizontal)
    {
        using var trackPath = XamlDrawing.RoundedRectangle(track, track.Height / 2f);
        if (Background is { } trackBrush && trackBrush.ApproximateColor.A > 0)
        {
            trackBrush.FillPath(graphics, trackPath);
        }

        if (Foreground is not { } barBrush || barBrush.ApproximateColor.A <= 0 || fraction <= 0f)
        {
            return;
        }

        var filled = horizontal
            ? new Rectangle(track.Left, track.Top, Math.Max(0, (int)Math.Round(track.Width * fraction)), track.Height)
            : new Rectangle(track.Left, track.Bottom - Math.Max(0, (int)Math.Round(track.Height * fraction)), track.Width, Math.Max(0, (int)Math.Round(track.Height * fraction)));
        if (filled.Width <= 0 || filled.Height <= 0)
        {
            return;
        }

        using var fillPath = XamlDrawing.RoundedRectangle(filled, Math.Min(track.Height / 2f, Math.Min(filled.Width, filled.Height) / 2f));
        barBrush.FillPath(graphics, fillPath);
    }

    private void PaintThumb(Graphics graphics, Rectangle bounds)
    {
        if (ThumbBrush is { } thumbBrush && thumbBrush.ApproximateColor.A > 0)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(bounds);
            thumbBrush.FillPath(graphics, path);
        }

        using var pen = new Pen(Color.FromArgb(0x55, 0, 0, 0));
        graphics.DrawEllipse(pen, bounds);
    }

    private double Fraction()
    {
        var range = Maximum - Minimum;
        if (range <= 0d)
        {
            return 0d;
        }

        return Clamp((Value - Minimum) / range, 0d, 1d);
    }

    private void SetValueFromPoint(Point location)
    {
        var bounds = XamlLayout.ContentBounds(this);
        var thumb = Math.Max(4, ThumbSize);
        double fraction;
        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = Math.Max(1, bounds.Width - thumb);
            fraction = (location.X - bounds.Left - (thumb / 2d)) / trackWidth;
        }
        else
        {
            var trackHeight = Math.Max(1, bounds.Height - thumb);
            fraction = 1d - ((location.Y - bounds.Top - (thumb / 2d)) / trackHeight);
        }

        Value = Coerce(Minimum + (Clamp(fraction, 0d, 1d) * (Maximum - Minimum)));
    }

    private double Coerce(double value)
    {
        var minimum = Math.Min(Minimum, Maximum);
        var maximum = Math.Max(Minimum, Maximum);
        value = Clamp(value, minimum, maximum);
        if (IsSnapToTickEnabled && TickFrequency > 0d)
        {
            var steps = Math.Round((value - minimum) / TickFrequency);
            value = minimum + (steps * TickFrequency);
        }

        return Clamp(value, minimum, maximum);
    }

    private static object? CoerceValue(System.Windows.Forms.Control target, object? value)
    {
        if (target is Slider slider && value is double number)
        {
            return Clamp(number, Math.Min(slider.Minimum, slider.Maximum), Math.Max(slider.Minimum, slider.Maximum));
        }

        return value;
    }

    private static void OnRangePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Slider slider && !slider.IsDisposed)
        {
            var coerced = (double)CoerceValue(slider, slider.Value)!;
            if (!coerced.Equals(slider.Value))
            {
                slider.Value = coerced;
            }

            slider.Invalidate();
        }
    }

    private static void OnValuePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Slider slider && !slider.IsDisposed)
        {
            slider.Invalidate();
            slider.ValueChanged?.Invoke(slider, EventArgs.Empty);
        }
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }
}
