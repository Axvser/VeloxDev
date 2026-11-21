using System.Drawing;
using System.Drawing.Drawing2D;

namespace VeloxDev.WinForms.Xaml;

/// <summary>The rule a <see cref="GeometryGroup"/> applies when its child geometries overlap.</summary>
public enum FillRule
{
    /// <summary>Alternating regions are inside and outside.</summary>
    EvenOdd,

    /// <summary>A region is inside when its winding number is non-zero.</summary>
    Nonzero,
}

/// <summary>
/// The shared contract for vector geometry: turning a rectangle of space into a <see cref="GraphicsPath"/>.
/// </summary>
/// <remarks>
/// The rectangle passed to <see cref="ToPath(RectangleF)"/> is the space a drawing gives the geometry. Geometries
/// with intrinsic coordinates, such as <see cref="LineGeometry"/> and <see cref="PathGeometry"/>, keep those
/// coordinates; geometries that have no rectangle of their own fill the supplied one.
/// </remarks>
public abstract class Geometry
{
    /// <summary>A geometry that produces an empty path.</summary>
    public static Geometry Empty { get; } = new EmptyGeometry();

    /// <summary>The rectangle this geometry occupies; an empty rectangle when it only fills a supplied one.</summary>
    public virtual RectangleF Bounds => RectangleF.Empty;

    /// <summary>Creates the GDI+ path for <paramref name="bounds"/>.</summary>
    /// <param name="bounds">The space the drawing gives this geometry.</param>
    public abstract GraphicsPath ToPath(RectangleF bounds);

    /// <summary>Parses SVG path data into a <see cref="PathGeometry"/>.</summary>
    /// <param name="data">The path data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public static Geometry Parse(string data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return new PathGeometry { Data = data };
    }

    private sealed class EmptyGeometry : Geometry
    {
        public override GraphicsPath ToPath(RectangleF bounds) => new();
    }
}

/// <summary>A straight segment from <see cref="StartPoint"/> to <see cref="EndPoint"/>.</summary>
public sealed class LineGeometry : Geometry
{
    /// <summary>The start of the segment.</summary>
    public PointF StartPoint { get; set; }

    /// <summary>The end of the segment.</summary>
    public PointF EndPoint { get; set; }

    /// <inheritdoc />
    public override RectangleF Bounds => RectangleF.FromLTRB(
        Math.Min(StartPoint.X, EndPoint.X),
        Math.Min(StartPoint.Y, EndPoint.Y),
        Math.Max(StartPoint.X, EndPoint.X),
        Math.Max(StartPoint.Y, EndPoint.Y));

    /// <inheritdoc />
    public override GraphicsPath ToPath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddLine(StartPoint, EndPoint);
        return path;
    }
}

/// <summary>A rectangle with independently rounded corners.</summary>
public sealed class RectangleGeometry : Geometry
{
    /// <summary>The rectangle in its own coordinates; an empty rectangle fills the supplied bounds instead.</summary>
    public RectangleF Rect { get; set; }

    /// <summary>The horizontal corner radius in pixels.</summary>
    public float RadiusX { get; set; }

    /// <summary>The vertical corner radius in pixels.</summary>
    public float RadiusY { get; set; }

    /// <inheritdoc />
    public override RectangleF Bounds => Rect;

    /// <inheritdoc />
    public override GraphicsPath ToPath(RectangleF bounds)
    {
        var rect = Rect.IsEmpty ? bounds : Rect;
        var path = new GraphicsPath();
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return path;
        }

        var radiusX = Math.Min(Math.Max(0f, RadiusX), rect.Width / 2f);
        var radiusY = Math.Min(Math.Max(0f, RadiusY), rect.Height / 2f);
        if (radiusX <= 0f || radiusY <= 0f)
        {
            path.AddRectangle(rect);
            return path;
        }

        var diameterX = radiusX * 2f;
        var diameterY = radiusY * 2f;
        path.AddArc(rect.X, rect.Y, diameterX, diameterY, 180f, 90f);
        path.AddArc(rect.Right - diameterX, rect.Y, diameterX, diameterY, 270f, 90f);
        path.AddArc(rect.Right - diameterX, rect.Bottom - diameterY, diameterX, diameterY, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - diameterY, diameterX, diameterY, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}

/// <summary>An ellipse.</summary>
public sealed class EllipseGeometry : Geometry
{
    /// <summary>The ellipse's rectangle in its own coordinates; an empty rectangle fills the supplied bounds instead.</summary>
    public RectangleF Rect { get; set; }

    /// <inheritdoc />
    public override RectangleF Bounds => Rect;

    /// <inheritdoc />
    public override GraphicsPath ToPath(RectangleF bounds)
    {
        var rect = Rect.IsEmpty ? bounds : Rect;
        var path = new GraphicsPath();
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return path;
        }

        path.AddEllipse(rect);
        return path;
    }
}

/// <summary>Several geometries combined into one path.</summary>
public sealed class GeometryGroup : Geometry
{
    /// <summary>The child geometries, combined in declaration order.</summary>
    public IList<Geometry> Children { get; } = new List<Geometry>();

    /// <summary>The fill rule applied to overlapping children; <see cref="FillRule.EvenOdd"/> by default.</summary>
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    /// <inheritdoc />
    public override RectangleF Bounds
    {
        get
        {
            var result = RectangleF.Empty;
            var any = false;
            foreach (var child in Children)
            {
                if (child is null)
                {
                    continue;
                }

                var bounds = child.Bounds;
                if (bounds.Width <= 0f && bounds.Height <= 0f)
                {
                    continue;
                }

                result = any
                    ? RectangleF.FromLTRB(
                        Math.Min(result.Left, bounds.Left),
                        Math.Min(result.Top, bounds.Top),
                        Math.Max(result.Right, bounds.Right),
                        Math.Max(result.Bottom, bounds.Bottom))
                    : bounds;
                any = true;
            }

            return result;
        }
    }

    /// <inheritdoc />
    public override GraphicsPath ToPath(RectangleF bounds)
    {
        var path = new GraphicsPath
        {
            FillMode = FillRule == FillRule.Nonzero ? FillMode.Winding : FillMode.Alternate,
        };

        foreach (var child in Children)
        {
            if (child is null)
            {
                continue;
            }

            using var childPath = child.ToPath(bounds);
            if (childPath.PointCount > 0)
            {
                path.AddPath(childPath, connect: false);
            }
        }

        return path;
    }
}

/// <summary>A path written in the common SVG path syntax.</summary>
/// <remarks>
/// <see cref="Data"/> coordinates are used as authored; the rectangle a drawing supplies does not stretch them.
/// The same parser backs the <c>Path</c> shape control.
/// </remarks>
public sealed class PathGeometry : Geometry
{
    /// <summary>The path data in SVG syntax.</summary>
    public string Data { get; set; } = string.Empty;

    /// <inheritdoc />
    public override RectangleF Bounds
    {
        get
        {
            using var path = ToPath(RectangleF.Empty);
            return path.PointCount == 0 ? RectangleF.Empty : path.GetBounds();
        }
    }

    /// <inheritdoc />
    public override GraphicsPath ToPath(RectangleF bounds)
        => Controls.Shapes.SvgPathParser.Parse(Data);
}
