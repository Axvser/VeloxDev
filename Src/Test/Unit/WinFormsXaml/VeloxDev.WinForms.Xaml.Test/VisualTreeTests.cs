using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Controls;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlPen = VeloxDev.WinForms.Xaml.Pen;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>Runtime coverage for the opt-in lightweight visual tree.</summary>
[STATestClass]
public sealed class VisualTreeTests
{
    [TestMethod]
    public void VisualHost_RendersAShapeWithoutAChildControl()
    {
        using var host = new VisualHost { Size = new Size(100, 100) };
        host.Root.Add(new ShapeVisual
        {
            Bounds = new RectangleF(10f, 10f, 40f, 40f),
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
        });

        using var bitmap = Render(host, 100, 100);

        Assert.AreEqual(Color.Red.ToArgb(), bitmap.GetPixel(20, 20).ToArgb());
        Assert.AreEqual(0, host.Controls.Count, "visual nodes are not child windows");
    }

    [TestMethod]
    public void VisualHost_ComposesGroupOpacityAndTransform()
    {
        using var host = new VisualHost { Size = new Size(100, 100) };
        var group = new ContainerVisual
        {
            Bounds = new RectangleF(0f, 0f, 100f, 100f),
            Opacity = 0.5d,
        };
        var shape = new ShapeVisual
        {
            Bounds = new RectangleF(0f, 0f, 30f, 30f),
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
        };
        shape.Transform.Translate(20f, 0f);
        group.Add(shape);
        host.Root.Add(group);

        using var bitmap = Render(host, 100, 100);

        var moved = bitmap.GetPixel(30, 10);
        Assert.AreEqual(255, moved.R, "the transform moved the shape");
        Assert.IsGreaterThan(60, moved.G, "the group opacity blends with the white background");
        Assert.IsLessThan(220, moved.G);
        Assert.AreEqual(Color.White.ToArgb(), bitmap.GetPixel(5, 10).ToArgb());
    }

    [TestMethod]
    public void VisualHost_AppliesAClipInLocalCoordinates()
    {
        using var host = new VisualHost { Size = new Size(100, 100) };
        var group = new ContainerVisual
        {
            Bounds = new RectangleF(0f, 0f, 100f, 100f),
            Clip = new RectangleF(0f, 0f, 20f, 20f),
        };
        group.Add(new ShapeVisual
        {
            Bounds = new RectangleF(5f, 5f, 40f, 40f),
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
        });
        host.Root.Add(group);

        using var bitmap = Render(host, 100, 100);

        Assert.AreEqual(Color.Red.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.AreEqual(Color.White.ToArgb(), bitmap.GetPixel(30, 10).ToArgb());
    }

    [TestMethod]
    public void VisualHost_HitTestsTheDeepestVisual()
    {
        using var host = new VisualHost { Size = new Size(100, 100) };
        var shape = new ShapeVisual
        {
            Bounds = new RectangleF(10f, 10f, 40f, 40f),
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
        };
        host.Root.Add(shape);

        Assert.AreSame(shape, host.HitTestVisual(new Point(20, 20)));
        Assert.IsNull(host.HitTestVisual(new Point(80, 80)));
    }

    [TestMethod]
    public void CurveBatchVisual_DrawsManyCurvesAsOneVisual()
    {
        using var host = new VisualHost { Size = new Size(200, 100) };
        var batch = new CurveBatchVisual
        {
            Bounds = new RectangleF(0f, 0f, 200f, 100f),
            Stroke = new XamlPen(Color.Blue, 2f),
        };
        for (var index = 0; index < 100; index++)
        {
            batch.Add(
                new PointF(5f, 20f + index % 40),
                new PointF(60f, 10f),
                new PointF(140f, 90f),
                new PointF(195f, 50f));
        }

        host.Root.Add(batch);

        using var bitmap = Render(host, 200, 100);
        var painted = 0;
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != Color.White.ToArgb())
                {
                    painted++;
                }
            }
        }

        Assert.IsGreaterThan(0, painted, "the batch painted onto the host");
        Assert.AreEqual(0, host.Controls.Count);
    }

    private static Bitmap Render(VisualHost host, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            host.RenderTo(graphics, new Size(width, height));
        }

        return bitmap;
    }
}
