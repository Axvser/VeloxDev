using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Per-monitor DPI in the visual backend: layout and input stay logical while painting and native children are
/// physical, and the deterministic <see cref="VisualSurface.RenderTo"/> path never applies the DPI transform.
/// </summary>
[STATestClass]
public sealed class VisualSurfaceDpiTests
{
    [TestMethod]
    public void DecimalScale_ScalesLayoutPaintingAndInputTogether()
    {
        using var surface = new VisualSurface { Size = new Size(150, 150) };
        surface.DpiScaleOverride = 1.5f;

        var root = new VC.Border
        {
            Background = Color.Red,
            CornerRadius = 0f,
        };
        surface.Root = root;

        var presses = 0;
        root.PointerPressed += (_, _) => presses++;

        Assert.AreEqual(1.5f, surface.DpiScale, 0.0001f, "the override pins the simulated scale");
        Assert.AreEqual(new Size(100, 100), surface.LogicalClientSize, "150 physical pixels are 100 logical units at 1.5x");

        // DrawToBitmap 走真实的 OnPaint 路径，会应用 DPI 世界缩放。
        using var bitmap = new Bitmap(150, 150);
        surface.DrawToBitmap(bitmap, new Rectangle(0, 0, 150, 150));

        Assert.AreEqual(new RectangleF(0f, 0f, 100f, 100f), root.Bounds, "layout still uses logical units");
        Assert.AreEqual(
            Color.Red.ToArgb(),
            bitmap.GetPixel(145, 145).ToArgb(),
            "a 100x100 logical root covers 150x150 physical pixels");

        // 物理 149 换成逻辑 99.33，落在 100x100 内；不换算则会落到外面。
        surface.ProcessPointerDown(new PointF(149f, 149f), MouseButtons.Left);
        surface.ProcessPointerUp(new PointF(149f, 149f), MouseButtons.Left);
        Assert.AreEqual(1, presses, "the physical corner converts to a logical point inside the element");

        Assert.AreSame(root, surface.FindElementAt(new PointF(99f, 99f)));
        Assert.IsNull(surface.FindElementAt(new PointF(101f, 99f)), "outside the logical bounds");
    }

    [TestMethod]
    public void RenderTo_AlwaysTreatsSizeAsLogical()
    {
        using var surface = new VisualSurface { Size = new Size(150, 150) };
        surface.DpiScaleOverride = 1.5f;

        var root = new VC.Border { Background = Color.Red, CornerRadius = 0f };
        surface.Root = root;

        using var bitmap = new Bitmap(100, 100);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(100, 100));
        }

        Assert.AreEqual(
            Color.Red.ToArgb(),
            bitmap.GetPixel(95, 95).ToArgb(),
            "RenderTo renders logical coordinates at scale 1 regardless of the control DPI");
        Assert.AreEqual(new RectangleF(0f, 0f, 100f, 100f), root.Bounds);
    }

    [TestMethod]
    public void NativeChild_IsPositionedInPhysicalPixels()
    {
        using var surface = new VisualSurface { Size = new Size(300, 300) };
        surface.DpiScaleOverride = 1.5f;

        using var child = new Panel();
        var host = new VC.NativeControlHost
        {
            Child = child,
            Width = 100,
            Height = 60,
            HorizontalAlignment = XamlHorizontalAlignment.Left,
            VerticalAlignment = XamlVerticalAlignment.Top,
        };
        var root = new VC.Border { Padding = XamlThickness.Empty };
        root.Add(host);
        surface.Root = root;

        using var bitmap = new Bitmap(1, 1);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(200, 200));
        }

        Assert.AreEqual(
            new Rectangle(0, 0, 150, 90),
            child.Bounds,
            "a logical 100x60 host becomes a 150x90 native window at 1.5x");
    }

    [TestMethod]
    public void NoHandle_FallsBackToUnitScale()
    {
        using var surface = new VisualSurface { Size = new Size(123, 45) };

        Assert.AreEqual(1f, surface.DpiScale);
        Assert.AreEqual(new Size(123, 45), surface.LogicalClientSize);
    }
}
