using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls.Shapes;

/// <summary>The base of vector elements drawn by the visual backend.</summary>
/// <remarks>
/// A shape paints inside its arranged rectangle and never creates a window. <see cref="Fill"/> and
/// <see cref="Stroke"/> are brushes; a gradient is flattened to its representative colour by the pen path.
/// </remarks>
public abstract class ShapeElement : VisualElement
{
    /// <summary>The brush painted inside the shape.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(XamlBrush), typeof(ShapeElement),
        new PropertyMetadata(null) { AffectsRender = true });

    /// <summary>The brush the outline is stroked with.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(XamlBrush), typeof(ShapeElement),
        new PropertyMetadata(null) { AffectsRender = true });

    /// <summary>The outline width in pixels.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(float), typeof(ShapeElement),
        new PropertyMetadata(1f) { AffectsRender = true });

    /// <summary>Creates a non-hit-testable shape.</summary>
    protected ShapeElement()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The brush painted inside the shape.</summary>
    public XamlBrush? Fill
    {
        get => (XamlBrush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The brush the outline is stroked with.</summary>
    public XamlBrush? Stroke
    {
        get => (XamlBrush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>The outline width in pixels.</summary>
    public float StrokeThickness
    {
        get => (float)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var bounds = new RectangleF(
            0f,
            0f,
            Math.Max(0f, Bounds.Width),
            Math.Max(0f, Bounds.Height));
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        using var path = CreatePath(bounds);
        if (path.PointCount == 0)
        {
            return;
        }

        if (Fill is { ApproximateColor.A: > 0 } fill)
        {
            fill.FillPath(graphics, path);
        }

        if (Stroke is { ApproximateColor.A: > 0 } stroke && StrokeThickness > 0f)
        {
            using var pen = new Pen(stroke.ApproximateColor, StrokeThickness)
            {
                LineJoin = LineJoin.Round,
            };
            graphics.DrawPath(pen, path);
        }
    }

    /// <summary>Builds the path this shape paints inside <paramref name="bounds"/>.</summary>
    /// <param name="bounds">The arranged rectangle, starting at the local origin.</param>
    protected abstract GraphicsPath CreatePath(RectangleF bounds);
}

/// <summary>A rectangle, optionally with rounded corners.</summary>
public class Rectangle : ShapeElement
{
    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        if (CornerRadius > 0f)
        {
            return VeloxDev.WinForms.Xaml.Controls.XamlDrawing.RoundedRectangle(bounds, CornerRadius);
        }

        var path = new GraphicsPath();
        path.AddRectangle(bounds);
        return path;
    }
}

/// <summary>An ellipse that fills its arranged rectangle.</summary>
public class Ellipse : ShapeElement
{
    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddEllipse(bounds);
        return path;
    }
}

/// <summary>A straight line in local coordinates.</summary>
public class Line : ShapeElement
{
    /// <summary>The start point.</summary>
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(
        nameof(X1), typeof(float), typeof(Line),
        new PropertyMetadata(0f) { AffectsRender = true });

    /// <summary>The start point's vertical coordinate.</summary>
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(
        nameof(Y1), typeof(float), typeof(Line),
        new PropertyMetadata(0f) { AffectsRender = true });

    /// <summary>The end point's horizontal coordinate.</summary>
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(
        nameof(X2), typeof(float), typeof(Line),
        new PropertyMetadata(0f) { AffectsRender = true });

    /// <summary>The end point's vertical coordinate.</summary>
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(
        nameof(Y2), typeof(float), typeof(Line),
        new PropertyMetadata(0f) { AffectsRender = true });

    /// <summary>The start point's horizontal coordinate.</summary>
    public float X1
    {
        get => (float)GetValue(X1Property)!;
        set => SetValue(X1Property, value);
    }

    /// <summary>The start point's vertical coordinate.</summary>
    public float Y1
    {
        get => (float)GetValue(Y1Property)!;
        set => SetValue(Y1Property, value);
    }

    /// <summary>The end point's horizontal coordinate.</summary>
    public float X2
    {
        get => (float)GetValue(X2Property)!;
        set => SetValue(X2Property, value);
    }

    /// <summary>The end point's vertical coordinate.</summary>
    public float Y2
    {
        get => (float)GetValue(Y2Property)!;
        set => SetValue(Y2Property, value);
    }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddLine(X1, Y1, X2, Y2);
        return path;
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (Stroke is not { ApproximateColor.A: > 0 } stroke || StrokeThickness <= 0f)
        {
            return;
        }

        using var pen = new Pen(stroke.ApproximateColor, StrokeThickness);
        graphics.DrawLine(pen, X1, Y1, X2, Y2);
    }
}

/// <summary>A closed polygon through <see cref="Points"/>.</summary>
public class Polygon : ShapeElement
{
    /// <summary>The vertices in local coordinates.</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(PointF[]), typeof(Polygon),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The vertices in local coordinates.</summary>
    public PointF[]? Points
    {
        get => (PointF[]?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Points is not { Length: > 0 } points)
        {
            return Size.Empty;
        }

        var width = points.Max(static point => point.X);
        var height = points.Max(static point => point.Y);
        return new Size((int)Math.Ceiling(width), (int)Math.Ceiling(height));
    }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        if (Points is { Length: > 0 } points)
        {
            path.AddPolygon(points);
        }

        return path;
    }
}

/// <summary>An open polyline through <see cref="Points"/>.</summary>
public class Polyline : Polygon
{
    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        if (Points is { Length: > 0 } points)
        {
            path.AddLines(points);
        }

        return path;
    }
}

/// <summary>A path parsed from an SVG mini-language string.</summary>
public class Path : ShapeElement
{
    /// <summary>The SVG path data.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(string), typeof(Path),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The SVG path data.</summary>
    public string Data
    {
        get => (string)GetValue(DataProperty)!;
        set => SetValue(DataProperty, value ?? string.Empty);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        using var path = VeloxDev.WinForms.Xaml.Controls.Shapes.SvgPathParser.Parse(Data);
        return path.PointCount == 0
            ? Size.Empty
            : new Size(
                (int)Math.Ceiling(path.GetBounds().Right),
                (int)Math.Ceiling(path.GetBounds().Bottom));
    }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
        => VeloxDev.WinForms.Xaml.Controls.Shapes.SvgPathParser.Parse(Data);
}
