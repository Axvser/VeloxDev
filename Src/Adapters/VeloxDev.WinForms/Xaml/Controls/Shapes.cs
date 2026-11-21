using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls.Shapes;

using VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// The shared fill and stroke surface of the vector shape controls.
/// </summary>
/// <remarks>
/// A shape draws its geometry in the control's content rectangle. <see cref="Fill"/> paints the interior;
/// <see cref="Stroke"/> paints the outline with <see cref="StrokeThickness"/>.
/// </remarks>
public abstract class Shape : Control
{
    /// <summary>The interior brush; transparent by default.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(Shape),
        new PropertyMetadata(new SolidColorBrush(Color.Transparent), OnVisualPropertyChanged));

    /// <summary>The outline brush; black by default.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Shape),
        new PropertyMetadata(new SolidColorBrush(Color.Black), OnVisualPropertyChanged));

    /// <summary>The outline width in pixels; one by default.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(float),
        typeof(Shape),
        new PropertyMetadata(1f, OnVisualPropertyChanged));

    /// <summary>Gets or sets the interior brush.</summary>
    public Brush? Fill
    {
        get => GetValue(FillProperty) as Brush;
        set => SetValue(FillProperty, value);
    }

    /// <summary>Gets or sets the outline brush.</summary>
    /// <remarks>GDI+ pens are solid, so a gradient outline is flattened to its <see cref="Brush.ApproximateColor"/>.</remarks>
    public Brush? Stroke
    {
        get => GetValue(StrokeProperty) as Brush;
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Gets or sets the outline width in pixels.</summary>
    public float StrokeThickness
    {
        get => (float)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Creates the geometry for the current bounds.</summary>
    protected abstract GraphicsPath CreatePath(RectangleF bounds);

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = XamlLayout.ContentBounds(this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = CreatePath(bounds);
        PaintPath(e.Graphics, path, closed: true);
    }

    /// <summary>Fills and strokes one geometry.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="path">The geometry.</param>
    /// <param name="closed">Whether fill is allowed.</param>
    protected void PaintPath(Graphics graphics, GraphicsPath path, bool closed)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (closed && Fill is { } fill && fill.ApproximateColor.A > 0)
        {
            fill.FillPath(graphics, path);
        }

        if (Stroke is { } stroke && stroke.ApproximateColor.A > 0 && StrokeThickness > 0f)
        {
            using var pen = new Pen(stroke, StrokeThickness);
            pen.Draw(graphics, path);
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

/// <summary>A rectangle with optional rounded corners.</summary>
public class Rectangle : Shape
{
    /// <summary>The horizontal corner radius in pixels.</summary>
    public static readonly DependencyProperty RadiusXProperty = DependencyProperty.Register(
        nameof(RadiusX),
        typeof(float),
        typeof(Rectangle),
        new PropertyMetadata(0f, OnVisualPropertyChanged));

    /// <summary>The vertical corner radius in pixels.</summary>
    public static readonly DependencyProperty RadiusYProperty = DependencyProperty.Register(
        nameof(RadiusY),
        typeof(float),
        typeof(Rectangle),
        new PropertyMetadata(0f, OnVisualPropertyChanged));

    /// <summary>Gets or sets the horizontal corner radius.</summary>
    public float RadiusX
    {
        get => (float)GetValue(RadiusXProperty)!;
        set => SetValue(RadiusXProperty, value);
    }

    /// <summary>Gets or sets the vertical corner radius.</summary>
    public float RadiusY
    {
        get => (float)GetValue(RadiusYProperty)!;
        set => SetValue(RadiusYProperty, value);
    }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
        => XamlDrawing.RoundedRectangle(bounds, RadiusX, RadiusY);

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }
}

/// <summary>An ellipse that fills its content rectangle.</summary>
public class Ellipse : Shape
{
    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddEllipse(bounds);
        return path;
    }
}

/// <summary>A straight line between two points in the control's content rectangle.</summary>
public class Line : Shape
{
    /// <summary>The horizontal start coordinate.</summary>
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(
        nameof(X1), typeof(float), typeof(Line), new PropertyMetadata(0f, OnVisualPropertyChanged));

    /// <summary>The vertical start coordinate.</summary>
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(
        nameof(Y1), typeof(float), typeof(Line), new PropertyMetadata(0f, OnVisualPropertyChanged));

    /// <summary>The horizontal end coordinate.</summary>
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(
        nameof(X2), typeof(float), typeof(Line), new PropertyMetadata(0f, OnVisualPropertyChanged));

    /// <summary>The vertical end coordinate.</summary>
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(
        nameof(Y2), typeof(float), typeof(Line), new PropertyMetadata(0f, OnVisualPropertyChanged));

    /// <summary>Gets or sets the horizontal start coordinate.</summary>
    public float X1 { get => (float)GetValue(X1Property)!; set => SetValue(X1Property, value); }

    /// <summary>Gets or sets the vertical start coordinate.</summary>
    public float Y1 { get => (float)GetValue(Y1Property)!; set => SetValue(Y1Property, value); }

    /// <summary>Gets or sets the horizontal end coordinate.</summary>
    public float X2 { get => (float)GetValue(X2Property)!; set => SetValue(X2Property, value); }

    /// <summary>Gets or sets the vertical end coordinate.</summary>
    public float Y2 { get => (float)GetValue(Y2Property)!; set => SetValue(Y2Property, value); }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        path.AddLine(X1, Y1, X2, Y2);
        return path;
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        using var path = CreatePath(RectangleF.Empty);
        PaintPath(e.Graphics, path, closed: false);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
        => new(
            (int)Math.Ceiling(Math.Max(X1, X2)) + (int)Math.Ceiling(StrokeThickness),
            (int)Math.Ceiling(Math.Max(Y1, Y2)) + (int)Math.Ceiling(StrokeThickness));

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }
}

/// <summary>A closed polygon built from a list of points in the control's own pixels.</summary>
public class Polygon : Shape
{
    /// <summary>The vertices, connected and closed in order.</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(PointF[]),
        typeof(Polygon),
        new PropertyMetadata(Array.Empty<PointF>(), OnPointsChanged));

    /// <summary>Gets or sets the vertices, connected and closed in order.</summary>
    public PointF[] Points
    {
        get => GetValue(PointsProperty) as PointF[] ?? Array.Empty<PointF>();
        set => SetValue(PointsProperty, value ?? Array.Empty<PointF>());
    }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        if (Points.Length >= 2)
        {
            path.AddPolygon(Points);
        }

        return path;
    }

    private static void OnPointsChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }
}

/// <summary>An open series of connected line segments in the control's own pixels.</summary>
/// <remarks>The stroke follows the segments as written; the fill closes the implied shape.</remarks>
public class Polyline : Shape
{
    /// <summary>The vertices, connected in order without closing the figure.</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(PointF[]),
        typeof(Polyline),
        new PropertyMetadata(Array.Empty<PointF>(), OnPointsChanged));

    /// <summary>Gets or sets the vertices, connected in order without closing the figure.</summary>
    public PointF[] Points
    {
        get => GetValue(PointsProperty) as PointF[] ?? Array.Empty<PointF>();
        set => SetValue(PointsProperty, value ?? Array.Empty<PointF>());
    }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = new GraphicsPath();
        if (Points.Length >= 2)
        {
            path.AddLines(Points);
        }

        return path;
    }

    private static void OnPointsChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }
}

/// <summary>
/// A vector path written in the common SVG path syntax.
/// </summary>
/// <remarks>
/// Coordinates are in the control's own pixels by default. Set <see cref="Stretch"/> to scale the parsed path into
/// the control instead. Supported commands are <c>M</c>, <c>L</c>, <c>H</c>, <c>V</c>, <c>C</c>, <c>S</c>,
/// <c>Q</c>, <c>T</c>, <c>A</c> and <c>Z</c>, in both absolute and relative forms.
/// </remarks>
public class Path : Shape
{
    /// <summary>The path data in SVG syntax.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(string),
        typeof(Path),
        new PropertyMetadata(string.Empty, OnDataPropertyChanged));

    /// <summary>How the parsed path is fitted into the control; <see cref="Stretch.None"/> by default.</summary>
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(Path),
        new PropertyMetadata(Stretch.None, OnDataPropertyChanged));

    /// <summary>Gets or sets the path data in SVG syntax.</summary>
    public string Data
    {
        get => (string?)GetValue(DataProperty) ?? string.Empty;
        set => SetValue(DataProperty, value);
    }

    /// <summary>Gets or sets how the parsed path is fitted into the control.</summary>
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <inheritdoc />
    protected override GraphicsPath CreatePath(RectangleF bounds)
    {
        var path = SvgPathParser.Parse(Data);
        if (Stretch == Stretch.None || path.PointCount == 0 || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return path;
        }

        var source = path.GetBounds();
        if (source.Width <= 0f || source.Height <= 0f)
        {
            return path;
        }

        var scaleX = bounds.Width / source.Width;
        var scaleY = bounds.Height / source.Height;
        if (Stretch is Stretch.Uniform or Stretch.UniformToFill)
        {
            var scale = Stretch == Stretch.Uniform ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
            scaleX = scale;
            scaleY = scale;
        }

        var offsetX = bounds.X + ((bounds.Width - (source.Width * scaleX)) / 2f);
        var offsetY = bounds.Y + ((bounds.Height - (source.Height * scaleY)) / 2f);

        // 直接用矩阵元素写 x' = (x - source.X) * scaleX + offsetX。用 Scale/Translate 叠加时
        // GDI+ 的行向量顺序会让 -source 不被缩放，整条路径平移出控件（小尺寸 SlotView 完全不可见）。
        using var transform = new Matrix(
            scaleX, 0f,
            0f, scaleY,
            offsetX - (source.X * scaleX),
            offsetY - (source.Y * scaleY));
        path.Transform(transform);
        return path;
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        using var path = SvgPathParser.Parse(Data);
        var bounds = path.GetBounds();
        return new Size(
            (int)Math.Ceiling(bounds.Right + StrokeThickness),
            (int)Math.Ceiling(bounds.Bottom + StrokeThickness));
    }

    private static void OnDataPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
            control.Parent?.PerformLayout();
        }
    }
}
