using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Two-axis scrolling in the HWND-free visual backend: horizontal offsets, thumb dragging, keyboard paging and
/// bringing an element into the viewport.
/// </summary>
[STATestClass]
public sealed class VisualScrollViewerTests
{
    [TestMethod]
    public void ScrollViewer_ExposesHorizontalOffsetsAndMovesContent()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        var content = new VC.Border { Width = 600, Height = 100, Padding = XamlThickness.Empty };
        view.Content = content;
        surface.Root = view;
        Layout(surface, 240, 160);

        Assert.AreEqual(600f, view.ExtentWidth, 0.1f);
        Assert.AreEqual(200f, view.ViewportWidth, 0.1f);
        Assert.AreEqual(400f, view.MaxHorizontalOffset, 0.1f);

        view.HorizontalOffset = 150f;
        Layout(surface, 240, 160);

        Assert.AreEqual(-150f, content.Bounds.X, 0.1f, "horizontal scrolling moves content to the left");
        Assert.AreEqual(0f, content.Bounds.Y, 0.1f, "vertical position is untouched");
    }

    [TestMethod]
    public void ScrollViewer_WheelScrollsHorizontallyWhenNothingScrollsVertically()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Border { Width = 600, Height = 100, Padding = XamlThickness.Empty };
        surface.Root = view;
        Layout(surface, 240, 160);

        var point = view.PointToSurface(new PointF(50f, 50f));
        surface.ProcessPointerWheel(point, -120);

        Assert.IsTrue(view.HorizontalOffset > 0f, "a wheel turn falls back to the horizontal axis");
        Assert.AreEqual(0f, view.VerticalOffset, 0.1f);
    }

    [TestMethod]
    public void ScrollViewer_DragsBothThumbsWithPointerCapture()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Canvas { Width = 600, Height = 1000 };
        surface.Root = view;
        Layout(surface, 240, 160);

        // 200/600 横向、100/1000 纵向；两条 thumb 都取最小长度 24，行程分别是 133.33 与 76。
        var horizontalStart = view.PointToSurface(new PointF(10f, 95f));
        var horizontalMove = view.PointToSurface(new PointF(40f, 95f));
        surface.ProcessPointerDown(horizontalStart, MouseButtons.Left);
        surface.ProcessPointerMove(horizontalMove);
        surface.ProcessPointerUp(horizontalMove, MouseButtons.Left);

        Assert.AreEqual(90f, view.HorizontalOffset, 1.5f, "30 px of drag on a 133.33 px travel maps to 90 px");

        var verticalStart = view.PointToSurface(new PointF(195f, 10f));
        var verticalMove = view.PointToSurface(new PointF(195f, 30f));
        surface.ProcessPointerDown(verticalStart, MouseButtons.Left);
        surface.ProcessPointerMove(verticalMove);
        surface.ProcessPointerUp(verticalMove, MouseButtons.Left);

        Assert.AreEqual(236.84f, view.VerticalOffset, 1.5f, "20 px of drag on a 76 px travel maps to 236.84 px");
    }

    [TestMethod]
    public void ScrollViewer_HandlesPageAndBoundaryKeys()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Canvas { Width = 200, Height = 1000 };
        surface.Root = view;
        Layout(surface, 240, 160);
        Assert.IsTrue(surface.FocusElement(view), "the viewer takes logical focus for keyboard paging");

        surface.ProcessKeyDown(Keys.PageDown);
        Assert.AreEqual(100f, view.VerticalOffset, 0.1f, "PageDown moves by one viewport");

        surface.ProcessKeyDown(Keys.End);
        Assert.AreEqual(900f, view.VerticalOffset, 0.1f);

        surface.ProcessKeyDown(Keys.Home);
        Assert.AreEqual(0f, view.VerticalOffset, 0.1f);
    }

    [TestMethod]
    public void ScrollViewer_BringsAnElementIntoViewOnBothAxes()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        var content = new VC.Canvas { Width = 600, Height = 600 };
        var target = new VC.Border { Width = 40, Height = 40, Padding = XamlThickness.Empty };
        VC.Canvas.SetLeft(target, 500);
        VC.Canvas.SetTop(target, 400);
        content.Add(target);
        view.Content = content;
        surface.Root = view;
        Layout(surface, 240, 160);

        view.ScrollIntoView(target);
        Layout(surface, 240, 160);

        Assert.AreEqual(340f, view.HorizontalOffset, 0.6f, "the right edge of the target lands on the viewport edge");
        Assert.AreEqual(340f, view.VerticalOffset, 0.6f, "the bottom edge of the target lands on the viewport edge");

        view.BringIntoView(target);
        Layout(surface, 240, 160);

        Assert.AreEqual(340f, view.HorizontalOffset, 0.6f, "an already visible element does not move again");
        Assert.AreEqual(340f, view.VerticalOffset, 0.6f);
    }

    private static VisualSurface CreateSurface(int width, int height)
        => new() { Size = new Size(width, height) };

    private static void Layout(VisualSurface surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }
}
