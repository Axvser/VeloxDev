using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A horizontal progress track filled to <see cref="Value"/>.</summary>
public class ProgressBar : VisualElement
{
    /// <summary>The lowest value.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ProgressBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The highest value.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ProgressBar),
        new PropertyMetadata(100d) { AffectsRender = true });

    /// <summary>The current value, coerced into the range.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ProgressBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>Creates a non-interactive bar.</summary>
    public ProgressBar()
    {
        IsHitTestVisible = false;
        MinHeight = 6f;
    }

    /// <summary>The lowest value.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The highest value.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The current value.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, XamlMath.Clamp(value, Minimum, Maximum));
    }

    /// <summary>The covered fraction in the range 0..1.</summary>
    protected double Fraction
        => Maximum <= Minimum ? 0d : XamlMath.Clamp((Value - Minimum) / (Maximum - Minimum), 0d, 1d);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new(
            availableSize.Width > 0 ? availableSize.Width : 120,
            Math.Max(6, Math.Max(0, availableSize.Height)));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var bounds = new RectangleF(0f, 0f, Math.Max(0f, Bounds.Width), Math.Max(0f, Bounds.Height));
        var radius = Math.Max(1f, bounds.Height / 2f);
        using (var track = new SolidBrush(theme.SurfaceAlt))
        using (var path = XamlDrawing.RoundedRectangle(bounds, radius))
        {
            graphics.FillPath(track, path);
        }

        var width = (float)(bounds.Width * Fraction);
        if (width <= 0f)
        {
            return;
        }

        using var fill = new SolidBrush(theme.Accent);
        using var fillPath = XamlDrawing.RoundedRectangle(
            new RectangleF(0f, 0f, width, bounds.Height),
            radius);
        graphics.FillPath(fill, fillPath);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ValueProperty))
        {
            var coerced = XamlMath.Clamp(Value, Minimum, Maximum);
            if (Math.Abs(coerced - Value) > double.Epsilon)
            {
                SetValue(ValueProperty, coerced);
            }
        }
    }
}

/// <summary>A horizontal slider with a draggable thumb.</summary>
public class Slider : VisualElement
{
    /// <summary>The lowest value.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Slider),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The highest value.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Slider),
        new PropertyMetadata(100d) { AffectsRender = true });

    /// <summary>The current value, coerced into the range.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Slider),
        new PropertyMetadata(0d) { AffectsRender = true });

    private bool _dragging;
    private const float ThumbRadius = 7f;
    private const float TrackHeight = 4f;

    /// <summary>Creates a focusable slider.</summary>
    public Slider()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
        MinHeight = 24f;
    }

    /// <summary>Raised after <see cref="Value"/> changes.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>The lowest value.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The highest value.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The current value.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, XamlMath.Clamp(value, Minimum, Maximum));
    }

    private double Fraction
        => Maximum <= Minimum ? 0d : XamlMath.Clamp((Value - Minimum) / (Maximum - Minimum), 0d, 1d);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new(
            availableSize.Width > 0 ? availableSize.Width : 160,
            Math.Max(Theme.Current.ControlHeight, (int)(ThumbRadius * 2f + 6f)));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var center = Bounds.Height / 2f;
        var track = new RectangleF(ThumbRadius, center - (TrackHeight / 2f), Math.Max(0f, Bounds.Width - (ThumbRadius * 2f)), TrackHeight);
        using (var background = new SolidBrush(theme.SurfaceAlt))
        using (var path = XamlDrawing.RoundedRectangle(track, TrackHeight / 2f))
        {
            graphics.FillPath(background, path);
        }

        var thumbX = track.Left + (float)(track.Width * Fraction);
        var active = new RectangleF(track.Left, track.Top, Math.Max(0f, thumbX - track.Left), track.Height);
        if (active.Width > 0f)
        {
            using var fill = new SolidBrush(theme.Accent);
            using var path = XamlDrawing.RoundedRectangle(active, TrackHeight / 2f);
            graphics.FillPath(fill, path);
        }

        using (var thumb = new SolidBrush(IsEnabled ? theme.Accent : theme.DisabledForeground))
        {
            graphics.FillEllipse(thumb, thumbX - ThumbRadius, center - ThumbRadius, ThumbRadius * 2f, ThumbRadius * 2f);
        }

        if (IsFocusVisible)
        {
            using var pen = new Pen(theme.FocusRing, 1.4f);
            graphics.DrawEllipse(pen, thumbX - ThumbRadius - 2f, center - ThumbRadius - 2f, (ThumbRadius * 2f) + 4f, (ThumbRadius * 2f) + 4f);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        _dragging = true;
        SetFromX(args.GetPosition(this).X);
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        if (_dragging)
        {
            SetFromX(args.GetPosition(this).X);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        _dragging = false;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        var step = (Maximum - Minimum) / 100d;
        switch (args.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                Value -= step;
                args.Handled = true;
                break;
            case Keys.Right:
            case Keys.Up:
                Value += step;
                args.Handled = true;
                break;
            case Keys.PageDown:
                Value -= step * 10d;
                args.Handled = true;
                break;
            case Keys.PageUp:
                Value += step * 10d;
                args.Handled = true;
                break;
            case Keys.Home:
                Value = Minimum;
                args.Handled = true;
                break;
            case Keys.End:
                Value = Maximum;
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ValueProperty))
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetFromX(float x)
    {
        var width = Math.Max(1f, Bounds.Width - (ThumbRadius * 2f));
        var fraction = XamlMath.Clamp((x - ThumbRadius) / width, 0f, 1f);
        Value = Minimum + ((Maximum - Minimum) * fraction);
    }
}

/// <summary>A button that repeats its click while the pointer stays down.</summary>
public class RepeatButton : Button
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private const int Delay = 400;
    private const int Interval = 80;

    /// <summary>Creates a repeating button.</summary>
    public RepeatButton()
    {
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Interval;
            OnClick();
        };
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (IsEnabled && args.Button == MouseButtons.Left)
        {
            _timer.Interval = Delay;
            _timer.Start();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        _timer.Stop();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        if (Control.MouseButtons != MouseButtons.Left)
        {
            _timer.Stop();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
