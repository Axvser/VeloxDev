using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The WPF-shaped scroll primitives of the HWND-free backend: <see cref="VC.ScrollBar"/> clamping, thumb drags,
/// track paging, keyboard paging and templates, and <see cref="VC.ScrollContentPresenter"/> extent, offsets and clip.
/// </summary>
[STATestClass]
public sealed class VisualScrollBarPresenterTests
{
    [TestMethod]
    public void ScrollBar_ClampsValueToTheViewportRange()
    {
        using var bar = new VC.ScrollBar { Maximum = 600, ViewportSize = 200 };
        var changes = 0;
        bar.ValueChanged += (_, _) => changes++;

        bar.Value = 1000;
        Assert.AreEqual(400d, bar.Value, 0.001d, "Maximum - ViewportSize caps the offset");

        var before = changes;
        bar.Value = 5000;
        Assert.AreEqual(400d, bar.Value, 0.001d);
        Assert.AreEqual(before, changes, "clamping to the same value raises no ValueChanged");

        bar.Minimum = 50;
        Assert.AreEqual(400d, bar.Value, 0.001d, "the value stays inside the new range");

        bar.ViewportSize = 400;
        Assert.AreEqual(200d, bar.Value, 0.001d, "a larger viewport shortens the range and re-clamps Value");

        bar.ViewportSize = 0;
        Assert.AreEqual(200d, bar.Value, 0.001d, "without a viewport the whole maximum is scrollable");

        bar.Maximum = 100;
        Assert.AreEqual(100d, bar.Value, 0.001d, "the value follows a shrinking maximum");
    }

    [TestMethod]
    public void ScrollBar_DragMovesTheThumbAndTheValue()
    {
        using var surface = CreateSurface(8, 100);
        var bar = new VC.ScrollBar { Width = 8, Height = 100, Maximum = 1000, ViewportSize = 300 };
        surface.Root = bar;
        Layout(surface, 8, 100);

        // 轨道 100、拇指 30（300/1000）、行程 70；35 px 拖动是可用范围 700 的一半。
        var start = bar.PointToSurface(new PointF(3f, 10f));
        var move = bar.PointToSurface(new PointF(3f, 45f));
        surface.ProcessPointerDown(start, MouseButtons.Left);
        surface.ProcessPointerMove(move);
        surface.ProcessPointerUp(move, MouseButtons.Left);

        Assert.AreEqual(350d, bar.Value, 0.6d);
    }

    [TestMethod]
    public void ScrollBar_HorizontalOrientationUsesTheXAxis()
    {
        using var surface = CreateSurface(200, 8);
        var bar = new VC.ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Width = 200,
            Height = 8,
            Maximum = 1000,
            ViewportSize = 300,
        };
        surface.Root = bar;
        Layout(surface, 200, 8);

        // 轨道 200、拇指 60、行程 140；30 px 拖动是可用范围 700 的 150。
        var start = bar.PointToSurface(new PointF(10f, 3f));
        var move = bar.PointToSurface(new PointF(40f, 3f));
        surface.ProcessPointerDown(start, MouseButtons.Left);
        surface.ProcessPointerMove(move);
        surface.ProcessPointerUp(move, MouseButtons.Left);

        Assert.AreEqual(150d, bar.Value, 0.6d);
    }

    [TestMethod]
    public void ScrollBar_TrackPressPagesThenContinuesAsTheSameDrag()
    {
        using var surface = CreateSurface(8, 100);
        var bar = new VC.ScrollBar
        {
            Width = 8,
            Height = 100,
            Maximum = 1000,
            ViewportSize = 300,
            LargeChange = 150,
        };
        surface.Root = bar;
        Layout(surface, 8, 100);

        var press = bar.PointToSurface(new PointF(3f, 80f));
        surface.ProcessPointerDown(press, MouseButtons.Left);
        Assert.AreEqual(150d, bar.Value, 0.6d, "a track press pages by LargeChange");

        var move = bar.PointToSurface(new PointF(3f, 90f));
        surface.ProcessPointerMove(move);
        surface.ProcessPointerUp(move, MouseButtons.Left);

        Assert.AreEqual(250d, bar.Value, 0.6d, "the paging press keeps the gesture, so the same drag moves the thumb");
    }

    [TestMethod]
    public void ScrollBar_KeyboardPagesAndJumpsToTheBounds()
    {
        using var surface = CreateSurface(8, 100);
        var bar = new VC.ScrollBar
        {
            Width = 8,
            Height = 100,
            Maximum = 1000,
            ViewportSize = 300,
            LargeChange = 100,
            SmallChange = 16,
        };
        surface.Root = bar;
        Layout(surface, 8, 100);
        Assert.IsTrue(surface.FocusElement(bar), "the bar takes logical focus for keyboard paging");

        surface.ProcessKeyDown(Keys.PageDown);
        Assert.AreEqual(100d, bar.Value, 0.001d, "PageDown moves by LargeChange");

        surface.ProcessKeyDown(Keys.End);
        Assert.AreEqual(700d, bar.Value, 0.001d);

        surface.ProcessKeyDown(Keys.Home);
        Assert.AreEqual(0d, bar.Value, 0.001d);

        surface.ProcessKeyDown(Keys.PageUp);
        Assert.AreEqual(0d, bar.Value, 0.001d, "PageUp at the top stays clamped");

        surface.ProcessKeyDown(Keys.Down);
        Assert.AreEqual(16d, bar.Value, 0.001d, "an arrow key moves by SmallChange");
    }

    [TestMethod]
    public void ScrollBar_ThumbBrightensUnderThePointer()
    {
        using var surface = CreateSurface(8, 100);
        var bar = new VC.ScrollBar { Width = 8, Height = 100, Maximum = 1000, ViewportSize = 300 };
        surface.Root = bar;
        Layout(surface, 8, 100);

        var normal = RenderPixel(surface, 8, 100, 3, 10);
        surface.ProcessPointerMove(new PointF(3f, 50f));
        var hovered = RenderPixel(surface, 8, 100, 3, 10);

        Assert.IsTrue(hovered.GetBrightness() > normal.GetBrightness() + 0.1f,
            "the thumb gets a highlight while the pointer is over its track");
    }

    [TestMethod]
    public void ScrollBar_ReservedCornerIsNotPartOfTheTrack()
    {
        using var surface = CreateSurface(8, 100);
        var bar = new VC.ScrollBar
        {
            Width = 8,
            Height = 100,
            Maximum = 1000,
            ViewportSize = 300,
            ReserveCornerSize = 8,
        };
        surface.Root = bar;
        Layout(surface, 8, 100);

        Assert.AreSame(bar, surface.FindElementAt(new PointF(3f, 50f)), "the track is hit");
        Assert.IsNull(surface.FindElementAt(new PointF(3f, 97f)),
            "the corner reserved for the crossing bar is not part of the track");
    }

    [TestMethod]
    public void ScrollBar_TemplatePartsPositionTheThumb()
    {
        using var surface = CreateSurface(8, 100);
        var bar = new VC.ScrollBar { Width = 8, Height = 100, Maximum = 1000, ViewportSize = 300 };
        VC.Border? thumb = null;
        var template = new VisualControlTemplate(scope =>
        {
            var track = new VC.Border { Width = 8, Height = 100, Padding = XamlThickness.Empty };
            thumb = new VC.Border { Padding = XamlThickness.Empty, Background = Color.Lime };
            scope.Register("PART_Track", track);
            scope.Register("PART_Thumb", thumb);

            var root = new VC.Canvas();
            root.Add(track);
            root.Add(thumb);
            return root;
        })
        {
            TargetType = typeof(VC.ScrollBar),
        };

        bar.Template = template;
        surface.Root = bar;
        Layout(surface, 8, 100);

        var partThumb = bar.GetTemplateChild<VC.Border>("PART_Thumb");
        Assert.IsNotNull(partThumb);
        Assert.AreSame(thumb, partThumb);
        Assert.AreSame(bar, partThumb!.TemplatedParent, "the template tree points at the bar");

        Assert.AreEqual(30f, partThumb.Bounds.Height, 0.5f, "the thumb is 300/1000 of the 100 px track");
        Assert.AreEqual(0f, partThumb.Bounds.Y, 0.5f);

        bar.Value = 350;
        Layout(surface, 8, 100);

        Assert.AreEqual(35f, partThumb.Bounds.Y, 0.5f, "the thumb travels half of the 70 px travel");
    }

    [TestMethod]
    public void ScrollContentPresenter_MeasuresExtentAndArrangesAtTheOffset()
    {
        using var surface = CreateSurface(220, 220);
        var presenter = new VC.ScrollContentPresenter { Width = 100, Height = 100 };
        var content = new VC.Canvas { Width = 300, Height = 400 };
        presenter.Content = content;
        surface.Root = presenter;
        Layout(surface, 220, 220);

        Assert.AreEqual(300f, presenter.ExtentWidth, 0.1f);
        Assert.AreEqual(400f, presenter.ExtentHeight, 0.1f);
        Assert.AreEqual(100f, presenter.ViewportWidth, 0.1f);
        Assert.AreEqual(100f, presenter.ViewportHeight, 0.1f);
        Assert.AreEqual(300f, presenter.MaxVerticalOffset, 0.1f);

        presenter.ScrollToVerticalOffset(120f);
        Layout(surface, 220, 220);
        Assert.AreEqual(-120f, content.Bounds.Y, 0.1f, "content moves up by the vertical offset");

        presenter.ScrollToHorizontalOffset(100f);
        Layout(surface, 220, 220);
        Assert.AreEqual(0f, presenter.HorizontalOffset, 0.1f, "a disabled axis ignores the offset");
        Assert.AreEqual(0f, content.Bounds.X, 0.1f);

        presenter.CanHorizontallyScroll = true;
        Layout(surface, 220, 220);
        presenter.ScrollToHorizontalOffset(100f);
        Layout(surface, 220, 220);
        Assert.AreEqual(100f, presenter.HorizontalOffset, 0.1f);
        Assert.AreEqual(-100f, content.Bounds.X, 0.1f);

        presenter.ScrollToVerticalOffset(999f);
        Assert.AreEqual(300f, presenter.VerticalOffset, 0.1f, "offsets clamp to Extent - Viewport");
    }

    [TestMethod]
    public void ScrollContentPresenter_ClipsContentToTheViewport()
    {
        using var surface = CreateSurface(220, 220);
        var presenter = new VC.ScrollContentPresenter
        {
            Width = 100,
            Height = 100,
            CanHorizontallyScroll = true,
        };
        presenter.Content = new VC.Border
        {
            Width = 400,
            Height = 400,
            Padding = XamlThickness.Empty,
            Background = Color.Red,
        };
        surface.Root = presenter;
        Layout(surface, 220, 220);

        presenter.ScrollToOffsets(200f, 200f);
        Layout(surface, 220, 220);

        var inside = presenter.PointToSurface(new PointF(50f, 50f));
        var outside = presenter.PointToSurface(new PointF(150f, 150f));
        var insideColor = RenderPixel(surface, 220, 220, (int)inside.X, (int)inside.Y);
        var outsideColor = RenderPixel(surface, 220, 220, (int)outside.X, (int)outside.Y);

        Assert.AreEqual(Color.Red.ToArgb(), insideColor.ToArgb(), "the visible part of the content is painted");
        Assert.AreNotEqual(Color.Red.ToArgb(), outsideColor.ToArgb(), "content beyond the viewport is clipped");
    }

    [TestMethod]
    public void ScrollContentPresenter_RaisesScrollChangedWithDeltas()
    {
        using var surface = CreateSurface(220, 220);
        var presenter = new VC.ScrollContentPresenter { Width = 100, Height = 100 };
        presenter.Content = new VC.Canvas { Width = 300, Height = 400 };
        var events = new List<VC.ScrollChangedEventArgs>();
        presenter.ScrollChanged += (_, e) => events.Add(e);
        surface.Root = presenter;
        Layout(surface, 220, 220);

        Assert.IsTrue(events.Count > 0, "the first layout publishes the extent and viewport");
        var last = events[^1];
        Assert.AreEqual(400f, last.ExtentHeight, 0.1f);
        Assert.AreEqual(100f, last.ViewportHeight, 0.1f);

        events.Clear();
        presenter.ScrollToVerticalOffset(50f);
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(50f, events[0].VerticalChange, 0.1f);
    }

    private static Color RenderPixel(VisualHost surface, int width, int height, int x, int y)
    {
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
        return bitmap.GetPixel(x, y);
    }

    private static VisualHost CreateSurface(int width, int height)
        => new() { Size = new Size(width, height) };

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }
}
