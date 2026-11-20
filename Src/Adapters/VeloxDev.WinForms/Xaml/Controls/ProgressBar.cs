using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A self-drawn progress bar with dependency-property range and colours.
/// </summary>
/// <remarks>
/// <see cref="IsIndeterminate"/> runs a small sweep timer; the determinate mode fills
/// <c>(Value - Minimum) / (Maximum - Minimum)</c> of the track. Both modes repaint on the dependency-property change
/// pipeline, so a style or trigger can restyle them.
/// </remarks>
public class ProgressBar : Control
{
    /// <summary>The lowest value; zero by default.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(ProgressBar),
        new PropertyMetadata(0d, OnRangePropertyChanged));

    /// <summary>The value at which the bar is full; 100 by default.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(ProgressBar),
        new PropertyMetadata(100d, OnRangePropertyChanged));

    /// <summary>The current value; zero by default.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(ProgressBar),
        new PropertyMetadata(0d, OnVisualPropertyChanged, CoerceValue));

    /// <summary>Whether the bar shows an animated sweep instead of a fraction.</summary>
    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate),
        typeof(bool),
        typeof(ProgressBar),
        new PropertyMetadata(false, OnIndeterminatePropertyChanged));

    /// <summary>The colour of the filled part; a blue by default.</summary>
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush),
        typeof(Color),
        typeof(ProgressBar),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5), OnVisualPropertyChanged));

    /// <summary>The colour of the empty track; a light grey by default.</summary>
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Color),
        typeof(ProgressBar),
        new PropertyMetadata(Color.FromArgb(0x33, 0x80, 0x80, 0x80), OnVisualPropertyChanged));

    /// <summary>The corner radius in pixels; three by default.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(ProgressBar),
        new PropertyMetadata(3f, OnVisualPropertyChanged));

    private readonly System.Windows.Forms.Timer sweepTimer = new() { Interval = 30 };
    private float sweep;

    /// <summary>Creates the bar.</summary>
    public ProgressBar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        sweepTimer.Tick += (_, _) =>
        {
            sweep = (sweep + 0.035f) % 1f;
            Invalidate();
        };
    }

    /// <summary>Gets or sets the lowest value.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the value at which the bar is full.</summary>
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

    /// <summary>Gets or sets whether the bar animates instead of showing a fraction.</summary>
    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty)!;
        set => SetValue(IsIndeterminateProperty, value);
    }

    /// <summary>Gets or sets the colour of the filled part.</summary>
    public Color BarBrush
    {
        get => (Color)GetValue(BarBrushProperty)!;
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>Gets or sets the colour of the empty track.</summary>
    public Color TrackBrush
    {
        get => (Color)GetValue(TrackBrushProperty)!;
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Gets or sets the corner radius in pixels.</summary>
    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
        => new(
            proposedSize.Width > 0 ? proposedSize.Width : 160,
            12 + (int)Math.Ceiling(Padding.Vertical));

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = XamlLayout.ContentBounds(this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var trackPath = XamlDrawing.RoundedRectangle(bounds, CornerRadius);
        using (var trackBrush = new SolidBrush(TrackBrush))
        {
            e.Graphics.FillPath(trackBrush, trackPath);
        }

        if (TrackBrush.A <= 0 && BarBrush.A <= 0)
        {
            return;
        }

        var fill = IsIndeterminate
            ? new RectangleF(bounds.X + (bounds.Width * ((sweep * 1.6f) - 0.3f)), bounds.Y, bounds.Width * 0.3f, bounds.Height)
            : new RectangleF(
                bounds.X,
                bounds.Y,
                bounds.Width * (float)Fraction(),
                bounds.Height);

        var clipped = RectangleF.Intersect(fill, bounds);
        if (clipped.Width <= 0f || clipped.Height <= 0f)
        {
            return;
        }

        using var fillPath = XamlDrawing.RoundedRectangle(clipped, Math.Min(CornerRadius, clipped.Height / 2f));
        using var barBrush = new SolidBrush(BarBrush);
        e.Graphics.FillPath(barBrush, fillPath);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sweepTimer.Stop();
            sweepTimer.Dispose();
        }

        base.Dispose(disposing);
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

    private static object? CoerceValue(System.Windows.Forms.Control target, object? value)
    {
        if (target is ProgressBar bar && value is double number)
        {
            return Clamp(number, Math.Min(bar.Minimum, bar.Maximum), Math.Max(bar.Minimum, bar.Maximum));
        }

        return value;
    }

    private static void OnRangePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ProgressBar bar && !bar.IsDisposed)
        {
            var coerced = (double)CoerceValue(bar, bar.Value)!;
            if (!coerced.Equals(bar.Value))
            {
                bar.Value = coerced;
            }

            bar.Invalidate();
        }
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }

    private static void OnIndeterminatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ProgressBar bar || bar.IsDisposed)
        {
            return;
        }

        if (bar.IsIndeterminate)
        {
            bar.sweepTimer.Start();
        }
        else
        {
            bar.sweepTimer.Stop();
        }

        bar.Invalidate();
    }
}
