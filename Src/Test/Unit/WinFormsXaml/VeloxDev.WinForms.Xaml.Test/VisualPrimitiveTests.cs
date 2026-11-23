using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using V = VeloxDev.WinForms.Xaml.Controls;
using VShapes = VeloxDev.WinForms.Xaml.Visuals.Controls.Shapes;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The media primitives that sit beside the controls: brushes, pens, geometry, transforms, drawings and the
/// Polygon/Polyline shapes.
/// </summary>
[STATestClass]
public sealed class VisualPrimitiveTests
{
    [TestMethod]
    public void SolidColorBrush_PaintsItsColourAndReportsIt()
    {
        var brush = new SolidColorBrush(Color.FromArgb(255, 10, 20, 30));
        Assert.AreEqual(Color.FromArgb(255, 10, 20, 30), brush.ApproximateColor);

        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            brush.Paint(graphics, new RectangleF(0f, 0f, 8f, 8f));
        }

        var pixel = bitmap.GetPixel(4, 4);
        Assert.AreEqual(10, pixel.R);
        Assert.AreEqual(20, pixel.G);
        Assert.AreEqual(30, pixel.B);
        Assert.AreEqual(255, pixel.A);

        Brush implicitBrush = Color.Red;
        Color implicitColor = implicitBrush;

        Assert.AreEqual(typeof(SolidColorBrush), implicitBrush.GetType());
        Assert.AreEqual(Color.Red, implicitColor);
    }

    [TestMethod]
    public void SolidColorBrush_FoldsOpacityIntoTheAlphaChannel()
    {
        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            new SolidColorBrush(Color.Red) { Opacity = 0.5d }.Paint(graphics, new RectangleF(0f, 0f, 8f, 8f));
        }

        Assert.AreEqual(128, bitmap.GetPixel(4, 4).A);
    }

    [TestMethod]
    public void GradientBrushes_PaintWithoutThrowing()
    {
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);

        var linear = new LinearGradientBrush();
        linear.GradientStops.Add(new GradientStop(Color.Red, 0d));
        linear.GradientStops.Add(new GradientStop(Color.Blue, 1d));
        linear.Paint(graphics, new RectangleF(0f, 0f, 16f, 16f));

        Assert.AreNotEqual(Color.Transparent.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());

        using var ellipse = new GraphicsPath();
        ellipse.AddEllipse(2f, 2f, 12f, 12f);
        linear.FillPath(graphics, ellipse);

        var radial = new RadialGradientBrush();
        radial.GradientStops.Add(new GradientStop(Color.White, 0d));
        radial.GradientStops.Add(new GradientStop(Color.Black, 1d));
        radial.Paint(graphics, new RectangleF(0f, 0f, 16f, 16f));

        var centre = bitmap.GetPixel(8, 8);
        Assert.IsTrue(centre.R > 128, $"the radial centre should be the first stop, got {centre}");
    }

    [TestMethod]
    public void Geometries_ProduceNonEmptyPaths()
    {
        using (var line = new LineGeometry { StartPoint = new PointF(0f, 0f), EndPoint = new PointF(10f, 10f) }
                   .ToPath(RectangleF.Empty))
        {
            Assert.AreEqual(2, line.PointCount);
        }

        var rectangle = new RectangleGeometry
        {
            Rect = new RectangleF(0f, 0f, 10f, 10f),
            RadiusX = 2f,
            RadiusY = 2f,
        };
        using (var rectanglePath = rectangle.ToPath(RectangleF.Empty))
        {
            Assert.IsTrue(rectanglePath.PointCount > 0);
        }

        Assert.AreEqual(new RectangleF(0f, 0f, 10f, 10f), rectangle.Bounds);

        using (var ellipsePath = new EllipseGeometry { Rect = new RectangleF(0f, 0f, 10f, 10f) }
                   .ToPath(RectangleF.Empty))
        {
            Assert.IsTrue(ellipsePath.PointCount > 0);
        }

        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry { StartPoint = new PointF(0f, 0f), EndPoint = new PointF(10f, 10f) });
        group.Children.Add(new EllipseGeometry { Rect = new RectangleF(0f, 0f, 4f, 4f) });
        using (var groupPath = group.ToPath(RectangleF.Empty))
        {
            Assert.IsTrue(groupPath.PointCount > 2);
        }

        using (var parsed = new PathGeometry { Data = "M0,0 L10,0 L10,10 Z" }.ToPath(RectangleF.Empty))
        {
            Assert.IsTrue(parsed.PointCount > 0);
        }

        Assert.AreEqual(typeof(PathGeometry), Geometry.Parse("M0,0 L10,0").GetType());

        using (var empty = Geometry.Empty.ToPath(RectangleF.Empty))
        {
            Assert.AreEqual(0, empty.PointCount);
        }
    }

    [TestMethod]
    public void TransformGroups_ComposeInDeclarationOrder()
    {
        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform { X = 10d, Y = 0d });
        group.Children.Add(new ScaleTransform { ScaleX = 2d, ScaleY = 2d });

        var points = new[] { new PointF(1f, 0f) };
        using (var matrix = group.Matrix)
        {
            matrix.TransformPoints(points);
        }

        Assert.AreEqual(22d, (double)points[0].X, 0.001d);
        Assert.AreEqual(0d, (double)points[0].Y, 0.001d);
    }

    [TestMethod]
    public void ScaleAndRotateTransforms_UseTheirCentre()
    {
        var points = new[] { new PointF(5f, 10f) };
        using (var matrix = new ScaleTransform { ScaleX = 2d, ScaleY = 2d, CenterX = 10d, CenterY = 10d }.Matrix)
        {
            matrix.TransformPoints(points);
        }

        Assert.AreEqual(0d, (double)points[0].X, 0.001d);
        Assert.AreEqual(10d, (double)points[0].Y, 0.001d);

        points = new[] { new PointF(1f, 0f) };
        using (var matrix = new RotateTransform { Angle = 90d }.Matrix)
        {
            matrix.TransformPoints(points);
        }

        Assert.AreEqual(0d, (double)points[0].X, 0.001d);
        Assert.AreEqual(1d, (double)points[0].Y, 0.001d);

        using var identity = Transform.Identity.Matrix;
        Assert.IsTrue(identity.IsIdentity);

        using var explicitMatrix = new MatrixTransform { M11 = 3d, M22 = 4d, OffsetX = 5d, OffsetY = 6d }.Matrix;
        Assert.AreEqual(3d, (double)explicitMatrix.Elements[0], 0.001d);
        Assert.AreEqual(5d, (double)explicitMatrix.Elements[4], 0.001d);
    }

    [TestMethod]
    public void DrawingImage_RendersItsDrawingIntoABitmap()
    {
        var drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry { Rect = new RectangleF(0f, 0f, 32f, 32f) },
            Brush = new SolidColorBrush(Color.Red),
        };

        using var image = new DrawingImage { Drawing = drawing, Width = 32, Height = 32 };
        using var loaded = image.Load();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(32, loaded!.Width);

        using var bitmap = new Bitmap(loaded!);
        var pixel = bitmap.GetPixel(16, 16);
        Assert.AreEqual(255, pixel.R);
        Assert.AreEqual(255, pixel.A);
    }

    [TestMethod]
    public void BitmapImage_ReturnsAnAssignedImageAndLoadsAFile()
    {
        using var source = new Bitmap(4, 3);
        using var assigned = new BitmapImage { Image = source };

        Assert.AreSame(source, assigned.Load());

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        try
        {
            source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            using var fromFile = new BitmapImage { Path = path };
            using var loaded = fromFile.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(4, loaded!.Width);
            Assert.AreEqual(3, loaded!.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Polygon_And_Polyline_BuildTheirGeometryFromPoints()
    {
        using var polygon = new VShapes.Polygon
        {
            Points = [new PointF(0f, 0f), new PointF(10f, 0f), new PointF(5f, 10f)],
        };
        using var polygonPath = InvokeCreatePath(polygon, new RectangleF(0f, 0f, 20f, 20f));
        Assert.AreEqual(3, polygonPath.PointCount);
        Assert.AreNotEqual(0, polygonPath.PathTypes[polygonPath.PathTypes.Length - 1] & 0x80);

        using var polyline = new VShapes.Polyline
        {
            Points = [new PointF(0f, 0f), new PointF(10f, 0f), new PointF(10f, 10f)],
        };
        using var polylinePath = InvokeCreatePath(polyline, new RectangleF(0f, 0f, 20f, 20f));
        Assert.AreEqual(3, polylinePath.PointCount);
        Assert.AreEqual(0, polylinePath.PathTypes[polylinePath.PathTypes.Length - 1] & 0x80);
    }

    private static GraphicsPath InvokeCreatePath(object shape, RectangleF bounds)
    {
        var method = shape.GetType().GetMethod("CreatePath", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{shape.GetType().Name}.CreatePath moved; update this test.");

        return (GraphicsPath)method.Invoke(shape, new object[] { bounds })!;
    }
}
