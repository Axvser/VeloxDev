using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// The painting contract shared by <see cref="SolidColorBrush"/>, the gradient brushes and <see cref="ImageBrush"/>.
/// </summary>
/// <remarks>
/// <para>
/// A brush paints into an existing <see cref="Graphics"/> and owns no device context: the GDI+ brush is created for
/// the duration of a single paint and disposed immediately.
/// </para>
/// <para>
/// <see cref="Opacity"/> is folded into the alpha channel of every colour the brush emits, so a semi-transparent
/// brush and a colour with explicit alpha compose the same way.
/// </para>
/// </remarks>
public abstract class Brush
{
    /// <summary>The brush-wide opacity in the range 0..1; one by default.</summary>
    public double Opacity { get; set; } = 1d;

    /// <summary>A brush that paints nothing.</summary>
    public static Brush Empty { get; } = new EmptyBrush();

    /// <summary>The colour that best stands for this brush where GDI+ needs a single colour.</summary>
    public virtual Color ApproximateColor => Color.Transparent;

    /// <summary>The representative alpha channel; a compatibility shorthand for <c>ApproximateColor.A</c>.</summary>
    public byte A => ApproximateColor.A;

    /// <summary>The representative red channel; a compatibility shorthand for <c>ApproximateColor.R</c>.</summary>
    public byte R => ApproximateColor.R;

    /// <summary>The representative green channel; a compatibility shorthand for <c>ApproximateColor.G</c>.</summary>
    public byte G => ApproximateColor.G;

    /// <summary>The representative blue channel; a compatibility shorthand for <c>ApproximateColor.B</c>.</summary>
    public byte B => ApproximateColor.B;

    /// <summary>Paints this brush across <paramref name="bounds"/>.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="bounds">The rectangle to paint.</param>
    public abstract void Paint(Graphics graphics, RectangleF bounds);

    /// <summary>Paints this brush only inside <paramref name="path"/>.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="path">The region to paint; the area outside it is untouched.</param>
    /// <remarks>
    /// The default implementation clips the surface to <paramref name="path"/> and then paints its bounding
    /// rectangle. Brushes with a cheaper path-aware fill override it.
    /// </remarks>
    public virtual void FillPath(Graphics graphics, GraphicsPath path)
    {
        if (path.PointCount == 0)
        {
            return;
        }

        // 用 Graphics 自己保存的状态恢复裁剪区：Region.Clone 返回 object，且手动换回 Region 会牵扯所有权。
        var state = graphics.Save();
        graphics.SetClip(path, CombineMode.Intersect);
        try
        {
            Paint(graphics, path.GetBounds());
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <summary>Creates a solid brush from a <see cref="System.Drawing.Color"/>.</summary>
    /// <param name="color">The colour to paint.</param>
    public static implicit operator Brush(Color color) => new SolidColorBrush(color);

    /// <summary>Reads the representative colour of a brush; a null brush reads transparent.</summary>
    /// <param name="brush">The brush, or <see langword="null"/>.</param>
    public static implicit operator Color(Brush? brush) => brush?.ApproximateColor ?? Color.Transparent;

    /// <summary>Converts a stored value into a brush: a <see cref="Color"/>, a brush or a colour string.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The brush, or <see langword="null"/> for a null value.</returns>
    /// <exception cref="FormatException">A string is not a known colour.</exception>
    /// <exception cref="InvalidCastException">The value is not a colour, brush or string.</exception>
    public static Brush? FromValue(object? value)
    {
        return value switch
        {
            null => null,
            Brush brush => brush,
            Color color => new SolidColorBrush(color),
            string text => Parse(text),
            _ => throw new InvalidCastException(
                $"Cannot convert '{value}' ({value.GetType()}) to '{typeof(Brush)}'."),
        };
    }

    /// <summary>Parses a named or hexadecimal colour into a <see cref="SolidColorBrush"/>.</summary>
    /// <param name="text">The colour text understood by <see cref="XamlRuntime.ParseColor(string)"/>.</param>
    /// <exception cref="FormatException">The text is not a known colour.</exception>
    public static Brush Parse(string text) => new SolidColorBrush(XamlRuntime.ParseColor(text));

    /// <summary>Clamps <see cref="Opacity"/> and applies it to a colour's alpha channel.</summary>
    /// <param name="color">The colour to fade.</param>
    protected Color ApplyOpacity(Color color)
    {
        var opacity = Math.Min(1d, Math.Max(0d, Opacity));
        var alpha = (int)Math.Round(color.A * opacity);
        return alpha <= 0 ? Color.Transparent : Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private sealed class EmptyBrush : Brush
    {
        public override void Paint(Graphics graphics, RectangleF bounds)
        {
        }

        public override void FillPath(Graphics graphics, GraphicsPath path)
        {
        }
    }
}

/// <summary>A brush that paints one colour.</summary>
public class SolidColorBrush : Brush
{
    /// <summary>Creates a black brush.</summary>
    public SolidColorBrush()
    {
    }

    /// <summary>Creates a brush of <paramref name="color"/>.</summary>
    public SolidColorBrush(Color color)
    {
        Color = color;
    }

    /// <summary>The colour this brush paints.</summary>
    public Color Color { get; set; } = Color.Black;

    /// <inheritdoc />
    public override Color ApproximateColor => Color;

    /// <inheritdoc />
    public override void Paint(Graphics graphics, RectangleF bounds)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        var color = ApplyOpacity(Color);
        if (color.A <= 0)
        {
            return;
        }

        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, bounds);
    }

    /// <inheritdoc />
    public override void FillPath(Graphics graphics, GraphicsPath path)
    {
        var color = ApplyOpacity(Color);
        if (color.A <= 0 || path.PointCount == 0)
        {
            return;
        }

        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }
}

/// <summary>One colour stop of a gradient.</summary>
public class GradientStop
{
    /// <summary>Creates a transparent stop at offset zero.</summary>
    public GradientStop()
    {
    }

    /// <summary>Creates a stop at <paramref name="offset"/>.</summary>
    /// <param name="color">The stop colour.</param>
    /// <param name="offset">The position along the gradient, 0..1.</param>
    public GradientStop(Color color, double offset)
    {
        Color = color;
        Offset = offset;
    }

    /// <summary>The colour at this stop.</summary>
    public Color Color { get; set; } = Color.Transparent;

    /// <summary>The position along the gradient in the range 0..1.</summary>
    public double Offset { get; set; }
}

/// <summary>A gradient that runs in a straight line from <see cref="StartPoint"/> to <see cref="EndPoint"/>.</summary>
/// <remarks>
/// The two points are relative to the painted rectangle: <c>(0,0)</c> is its top-left and <c>(1,1)</c> its
/// bottom-right. Stops are sorted by <see cref="GradientStop.Offset"/> and spread over a single GDI+ colour blend.
/// </remarks>
public class LinearGradientBrush : Brush
{
    /// <summary>The gradient start, relative to the painted rectangle; top by default.</summary>
    public PointF StartPoint { get; set; } = new(0f, 0f);

    /// <summary>The gradient end, relative to the painted rectangle; bottom by default.</summary>
    public PointF EndPoint { get; set; } = new(0f, 1f);

    /// <summary>The colour stops, in declaration order; they are sorted by offset when painted.</summary>
    public IList<GradientStop> GradientStops { get; } = new List<GradientStop>();

    /// <inheritdoc />
    public override Color ApproximateColor => FirstStopColor();

    /// <inheritdoc />
    public override void Paint(Graphics graphics, RectangleF bounds)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        PaintCore(graphics, null, bounds);
    }

    /// <inheritdoc />
    public override void FillPath(Graphics graphics, GraphicsPath path)
    {
        if (path.PointCount == 0)
        {
            return;
        }

        PaintCore(graphics, path, path.GetBounds());
    }

    private void PaintCore(Graphics graphics, GraphicsPath? path, RectangleF bounds)
    {
        var blend = BuildBlend();
        if (blend is null)
        {
            FillSolid(graphics, path, bounds, ApplyOpacity(ApproximateColor));
            return;
        }

        var start = MapPoint(StartPoint, bounds);
        var end = MapPoint(EndPoint, bounds);
        if (start == end)
        {
            // GDI+ 拒绝零长度渐变；退化成首色填充。
            FillSolid(graphics, path, bounds, blend.Colors[0]);
            return;
        }

        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(start, end, Color.Black, Color.White)
        {
            InterpolationColors = blend,
        };
        if (path is null)
        {
            graphics.FillRectangle(brush, bounds);
        }
        else
        {
            graphics.FillPath(brush, path);
        }
    }

    private ColorBlend? BuildBlend()
    {
        var stops = SortedStops();
        if (stops.Count == 0)
        {
            return null;
        }

        var colors = new List<Color>();
        var positions = new List<float>();
        foreach (var stop in stops)
        {
            var position = (float)Math.Min(1d, Math.Max(0d, stop.Offset));
            if (positions.Count > 0 && position <= positions[positions.Count - 1])
            {
                continue;
            }

            colors.Add(ApplyOpacity(stop.Color));
            positions.Add(position);
        }

        if (colors.Count == 0)
        {
            return null;
        }

        if (positions.Count == 1)
        {
            return null;
        }

        if (positions[0] > 0f)
        {
            colors.Insert(0, colors[0]);
            positions.Insert(0, 0f);
        }

        if (positions[positions.Count - 1] < 1f)
        {
            colors.Add(colors[colors.Count - 1]);
            positions.Add(1f);
        }

        var blend = new ColorBlend(positions.Count)
        {
            Colors = colors.ToArray(),
            Positions = positions.ToArray(),
        };
        return blend;
    }

    private List<GradientStop> SortedStops()
    {
        var stops = new List<GradientStop>();
        foreach (var stop in GradientStops)
        {
            if (stop is not null)
            {
                stops.Add(stop);
            }
        }

        stops.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        return stops;
    }

    private Color FirstStopColor()
    {
        var stops = SortedStops();
        return stops.Count == 0 ? Color.Transparent : stops[0].Color;
    }

    private static PointF MapPoint(PointF relative, RectangleF bounds)
        => new(bounds.X + (relative.X * bounds.Width), bounds.Y + (relative.Y * bounds.Height));

    private static void FillSolid(Graphics graphics, GraphicsPath? path, RectangleF bounds, Color color)
    {
        if (color.A <= 0)
        {
            return;
        }

        using var brush = new SolidBrush(color);
        if (path is null)
        {
            graphics.FillRectangle(brush, bounds);
        }
        else
        {
            graphics.FillPath(brush, path);
        }
    }
}

/// <summary>A gradient that runs outward from <see cref="Center"/> over <see cref="Radius"/>.</summary>
/// <remarks>
/// <para>
/// <see cref="Center"/> is relative to the painted rectangle, and <see cref="Radius"/> is a fraction of the
/// rectangle's shorter side.
/// </para>
/// <para>
/// GDI+ has no multi-stop radial brush, so the first stop becomes the centre colour and the last stop the
/// surrounding colour; intermediate stops are ignored.
/// </para>
/// </remarks>
public class RadialGradientBrush : Brush
{
    /// <summary>The gradient centre, relative to the painted rectangle; the middle by default.</summary>
    public PointF Center { get; set; } = new(0.5f, 0.5f);

    /// <summary>The gradient radius as a fraction of the rectangle's shorter side; half by default.</summary>
    public float Radius { get; set; } = 0.5f;

    /// <summary>The colour stops, in declaration order; the first and last are used.</summary>
    public IList<GradientStop> GradientStops { get; } = new List<GradientStop>();

    /// <inheritdoc />
    public override Color ApproximateColor => FirstStopColor();

    /// <inheritdoc />
    public override void Paint(Graphics graphics, RectangleF bounds)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        PaintCore(graphics, null, bounds);
    }

    /// <inheritdoc />
    public override void FillPath(Graphics graphics, GraphicsPath path)
    {
        if (path.PointCount == 0)
        {
            return;
        }

        PaintCore(graphics, path, path.GetBounds());
    }

    private void PaintCore(Graphics graphics, GraphicsPath? path, RectangleF bounds)
    {
        var stops = SortedStops();
        if (stops.Count == 0)
        {
            return;
        }

        var center = new PointF(
            bounds.X + (Center.X * bounds.Width),
            bounds.Y + (Center.Y * bounds.Height));
        var radius = Math.Max(0f, Radius) * Math.Min(bounds.Width, bounds.Height);
        if (stops.Count == 1 || radius <= 0f)
        {
            FillSolid(graphics, path, bounds, ApplyOpacity(stops[0].Color));
            return;
        }

        using var ellipse = new GraphicsPath();
        ellipse.AddEllipse(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        using var brush = new System.Drawing.Drawing2D.PathGradientBrush(ellipse)
        {
            CenterPoint = center,
            CenterColor = ApplyOpacity(stops[0].Color),
            SurroundColors = new[] { ApplyOpacity(stops[stops.Count - 1].Color) },
        };

        if (path is null)
        {
            graphics.FillRectangle(brush, bounds);
        }
        else
        {
            graphics.FillPath(brush, path);
        }
    }

    private List<GradientStop> SortedStops()
    {
        var stops = new List<GradientStop>();
        foreach (var stop in GradientStops)
        {
            if (stop is not null)
            {
                stops.Add(stop);
            }
        }

        stops.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        return stops;
    }

    private Color FirstStopColor()
    {
        var stops = SortedStops();
        return stops.Count == 0 ? Color.Transparent : stops[0].Color;
    }

    private static void FillSolid(Graphics graphics, GraphicsPath? path, RectangleF bounds, Color color)
    {
        if (color.A <= 0)
        {
            return;
        }

        using var brush = new SolidBrush(color);
        if (path is null)
        {
            graphics.FillRectangle(brush, bounds);
        }
        else
        {
            graphics.FillPath(brush, path);
        }
    }
}

/// <summary>A brush that tiles or stretches a bitmap over the painted rectangle.</summary>
/// <remarks>
/// The bitmap comes from <see cref="ImageSource"/> on every paint; a <see cref="BitmapImage"/> caches its
/// loaded image, so a brush backed by one does not re-read the file each frame.
/// </remarks>
public class ImageBrush : Brush
{
    /// <summary>The bitmap to paint; a null source paints nothing.</summary>
    public ImageSource? ImageSource { get; set; }

    /// <summary>How the bitmap is fitted into the painted rectangle; <see cref="Stretch.Fill"/> by default.</summary>
    public Stretch Stretch { get; set; } = Stretch.Fill;

    /// <inheritdoc />
    public override void Paint(Graphics graphics, RectangleF bounds)
    {
        var image = ImageSource?.Load();
        if (image is null || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        var destination = ResolveDestination(bounds, image.Size);
        if (destination.Width <= 0f || destination.Height <= 0f)
        {
            return;
        }

        var opacity = Math.Min(1d, Math.Max(0d, Opacity));
        if (opacity >= 1d)
        {
            graphics.DrawImage(image, destination);
            return;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)opacity });
        graphics.DrawImage(
            image,
            new[]
            {
                new PointF(destination.Left, destination.Top),
                new PointF(destination.Right, destination.Top),
                new PointF(destination.Left, destination.Bottom),
            },
            new RectangleF(0f, 0f, image.Width, image.Height),
            GraphicsUnit.Pixel,
            attributes);
    }

    private RectangleF ResolveDestination(RectangleF bounds, Size source)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return bounds;
        }

        switch (Stretch)
        {
            case Stretch.None:
                return new RectangleF(
                    bounds.X,
                    bounds.Y,
                    Math.Min(source.Width, bounds.Width),
                    Math.Min(source.Height, bounds.Height));

            case Stretch.Fill:
                return bounds;

            default:
            {
                var scale = Stretch == Stretch.UniformToFill
                    ? Math.Max(bounds.Width / source.Width, bounds.Height / source.Height)
                    : Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
                var width = source.Width * scale;
                var height = source.Height * scale;
                return new RectangleF(
                    bounds.X + ((bounds.Width - width) / 2f),
                    bounds.Y + ((bounds.Height - height) / 2f),
                    width,
                    height);
            }
        }
    }
}
