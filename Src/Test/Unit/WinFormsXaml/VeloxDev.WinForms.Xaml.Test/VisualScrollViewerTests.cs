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
        Assert.AreEqual(192f, view.ViewportWidth, 0.1f, "the horizontal bar reduces the height, so the vertical bar appears");
        Assert.AreEqual(408f, view.MaxHorizontalOffset, 0.1f);

        view.HorizontalOffset = 150f;
        Layout(surface, 240, 160);

        Assert.AreEqual(-150f, content.Bounds.X, 0.1f, "horizontal scrolling moves content to the left");
        Assert.AreEqual(0f, content.Bounds.Y, 0.1f, "vertical position is untouched");
    }

    [TestMethod]
    public void ScrollViewer_AutoBarsSettleAfterTheFirstLayout()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Border { Width = 600, Height = 100, Padding = XamlThickness.Empty };
        surface.Root = view;

        // Auto 条在 presenter 的 Arrange 里才第一次知道范围；本轮里改 Visibility 必须留下失效，
        // 下一帧才能把条排进各自的轨道。以前 EnsureLayout 结尾会把这笔失效吞掉，条永远是 0 尺寸。
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        for (var pass = 0; pass < 3; pass++)
        {
            surface.RenderTo(graphics, new Size(240, 160));
        }

        Assert.AreEqual(VeloxDev.WinForms.Xaml.Controls.Visibility.Visible, view.HorizontalScrollBar!.Visibility);
        Assert.AreEqual(VeloxDev.WinForms.Xaml.Controls.Visibility.Visible, view.VerticalScrollBar!.Visibility);
        Assert.AreEqual(192f, view.ViewportWidth, 0.5f);
        Assert.AreEqual(92f, view.ViewportHeight, 0.5f);
        Assert.IsGreaterThan(0f, view.HorizontalScrollBar.Bounds.Height);
        Assert.IsGreaterThan(0f, view.VerticalScrollBar.Bounds.Width);
    }

    [TestMethod]
    public void ScrollViewer_FollowsChildSizeChanges()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        var content = new VC.Border { Width = 400, Height = 80, Padding = XamlThickness.Empty };
        view.Content = content;
        surface.Root = view;
        Layout(surface, 240, 160);

        Assert.AreEqual(400f, view.ExtentWidth, 0.5f);
        Assert.AreEqual(80f, view.ExtentHeight, 0.5f);

        // 子元素实际变大后，ScrollViewer 自己重测内容并把新的范围/条最大值推下去；工作流画布只是
        // 一个被表面改 Width/Height 的普通子元素，条的自适应来自控件层，不是相机。
        content.Width = 300;
        content.Height = 400;
        Layout(surface, 240, 160);

        Assert.AreEqual(300f, view.ExtentWidth, 0.5f);
        Assert.AreEqual(400f, view.ExtentHeight, 0.5f);
        Assert.AreEqual(400d, view.VerticalScrollBar!.Maximum, 0.5d);
    }

    [TestMethod]
    public void ScrollViewer_WheelScrollsHorizontallyWhenNothingScrollsVertically()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Border { Width = 600, Height = 80, Padding = XamlThickness.Empty };
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

        // 两条拇指都出现时，各自的轨道要给对面让出角落（6px 条 + 2px 内缩）：
        // 横向轨道 192、拇指 64、行程 128；纵向轨道 92、拇指 24、行程 68。
        var horizontalStart = view.PointToSurface(new PointF(10f, 95f));
        var horizontalMove = view.PointToSurface(new PointF(40f, 95f));
        surface.ProcessPointerDown(horizontalStart, MouseButtons.Left);
        surface.ProcessPointerMove(horizontalMove);
        surface.ProcessPointerUp(horizontalMove, MouseButtons.Left);

        Assert.AreEqual(93.75f, view.HorizontalOffset, 1.5f, "30 px of drag on the horizontal track maps to 93.75 px");

        var verticalStart = view.PointToSurface(new PointF(195f, 10f));
        var verticalMove = view.PointToSurface(new PointF(195f, 30f));
        surface.ProcessPointerDown(verticalStart, MouseButtons.Left);
        surface.ProcessPointerMove(verticalMove);
        surface.ProcessPointerUp(verticalMove, MouseButtons.Left);

        Assert.AreEqual(267.06f, view.VerticalOffset, 1.5f, "20 px of drag on the vertical track maps to 267.06 px");
    }

    [TestMethod]
    public void ScrollViewer_HandlesPageAndBoundaryKeys()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Canvas { Width = 190, Height = 1000 };
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

        Assert.AreEqual(348f, view.HorizontalOffset, 0.6f, "the right edge of the target lands on the viewport edge");
        Assert.AreEqual(348f, view.VerticalOffset, 0.6f, "the bottom edge of the target lands on the viewport edge");

        view.BringIntoView(target);
        Layout(surface, 240, 160);

        Assert.AreEqual(348f, view.HorizontalOffset, 0.6f, "an already visible element does not move again");
        Assert.AreEqual(348f, view.VerticalOffset, 0.6f);
    }

    [TestMethod]
    public void ScrollViewer_MeasuresStarContentAtItsNaturalHeight()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };

        // 单星号行的 Grid 只有拿到「不设限」的高度才会报告内容高度；按 0 量会把高度量没了，
        // 滚动条因此永远不出现。
        var content = new VC.Grid();
        content.Add(new VC.Border { Height = 400 });
        view.Content = content;
        surface.Root = view;
        Layout(surface, 240, 160);

        Assert.AreEqual(400f, view.ExtentHeight, 0.1f, "the star row reports its content height");
        Assert.AreEqual(300f, view.MaxVerticalOffset, 0.1f, "the viewer can scroll the overflow");
    }

    [TestMethod]
    public void ScrollViewer_ScrollBarAreaHitsTheViewerInsteadOfItsContent()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };

        // 内容自己会处理按下（TextBox/列表都是这样），滚动条又是画出来的；命中阶段必须先把
        // 拇指那一条判给 viewer，否则内容吃掉按下，拇指拖不动。
        view.Content = new VC.TextBox { Width = 200, Height = 600 };
        surface.Root = view;
        Layout(surface, 240, 160);

        var trackPoint = view.PointToSurface(new PointF(194f, 20f));
        Assert.AreSame(view.VerticalScrollBar, surface.FindElementAt(trackPoint),
            "the vertical scrollbar part owns its track, not the content underneath");
    }

    [TestMethod]
    public void ScrollViewer_ContentInsideTheViewportStillTakesThePress()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        var text = new VC.TextBox { Width = 200, Height = 600 };
        view.Content = text;
        surface.Root = view;
        Layout(surface, 240, 160);

        var point = view.PointToSurface(new PointF(50f, 20f));
        surface.ProcessPointerDown(point, MouseButtons.Left);

        Assert.AreSame(text, surface.FindElementAt(point), "only the scrollbar strip belongs to the viewer");
        Assert.IsTrue(text.IsFocused, "a press in the content focuses the field");
    }

    [TestMethod]
    public void ScrollViewer_TrackPressContinuesAsTheSameDrag()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Canvas { Width = 190, Height = 1000 };
        surface.Root = view;
        Layout(surface, 240, 160);

        var press = view.PointToSurface(new PointF(195f, 50f));
        var move = view.PointToSurface(new PointF(195f, 80f));
        surface.ProcessPointerDown(press, MouseButtons.Left);
        surface.ProcessPointerMove(move);
        surface.ProcessPointerUp(move, MouseButtons.Left);

        Assert.IsTrue(view.VerticalOffset > 150f,
            "the press that paged the track keeps the gesture, so the same drag moves the thumb");
    }

    [TestMethod]
    public void ScrollViewer_ScrollBarCornerIsNotShared()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Canvas { Width = 600, Height = 1000 };
        surface.Root = view;
        Layout(surface, 240, 160);

        Assert.IsTrue(view.IsOverScrollBar(new PointF(195f, 50f)), "vertical track");
        Assert.IsTrue(view.IsOverScrollBar(new PointF(50f, 95f)), "horizontal track");
        Assert.IsFalse(view.IsOverScrollBar(new PointF(196f, 96f)),
            "the corner is reserved once the other bar is present");
    }

    [TestMethod]
    public void ScrollViewer_ThumbBrightensUnderThePointer()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        view.Content = new VC.Canvas { Width = 190, Height = 1000 };
        surface.Root = view;
        Layout(surface, 240, 160);

        var thumb = view.PointToSurface(new PointF(195f, 10f));
        var normal = RenderPixel(surface, (int)thumb.X, (int)thumb.Y);
        surface.ProcessPointerMove(view.PointToSurface(new PointF(195f, 50f)));
        var hovered = RenderPixel(surface, (int)thumb.X, (int)thumb.Y);

        Assert.IsTrue(hovered.GetBrightness() > normal.GetBrightness() + 0.1f,
            "the fallback thumb gets a highlight while the pointer is over its track");
    }

    private static Color RenderPixel(VisualHost surface, int x, int y)
    {
        using var bitmap = new Bitmap(240, 160);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(240, 160));
        return bitmap.GetPixel(x, y);
    }

    [TestMethod]
    public void ScrollViewer_UsesCustomTemplateParts()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100 };
        VC.ScrollContentPresenter? presenter = null;
        VC.ScrollBar? verticalBar = null;

        view.Template = new VisualControlTemplate(scope =>
        {
            var root = new VC.Grid();
            presenter = new VC.ScrollContentPresenter { Name = "PART_ScrollContentPresenter" };
            scope.Register("PART_ScrollContentPresenter", presenter);
            verticalBar = new VC.ScrollBar
            {
                Name = "PART_VerticalScrollBar",
                Orientation = Orientation.Vertical,
                HorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment.Right,
            };
            scope.Register("PART_VerticalScrollBar", verticalBar);
            root.Add(presenter);
            root.Add(verticalBar);
            return root;
        })
        {
            TargetType = typeof(VC.ScrollViewer),
        };

        var content = new VC.Canvas { Width = 200, Height = 1000 };
        view.Content = content;
        surface.Root = view;
        Layout(surface, 240, 160);

        Assert.AreSame(presenter, view.ContentPresenter, "PART_ScrollContentPresenter is wired");
        Assert.AreSame(verticalBar, view.VerticalScrollBar, "PART_VerticalScrollBar is wired");
        Assert.AreEqual(1000f, view.ExtentHeight, 0.5f, "the presenter reports the content extent");

        view.VerticalOffset = 200f;
        Layout(surface, 240, 160);
        Assert.AreEqual(200f, view.VerticalOffset, 0.5f);
        Assert.AreEqual(-200f, content.Bounds.Y, 0.5f, "the custom presenter scrolls the content");
    }

    [TestMethod]
    public void ScrollViewer_CanContentScrollUsesTheContentsScrollInfo()
    {
        using var surface = CreateSurface(240, 160);
        var view = new VC.ScrollViewer { Width = 200, Height = 100, CanContentScroll = true };
        var content = new LogicalScroller();
        view.Content = content;
        surface.Root = view;
        Layout(surface, 240, 160);

        Assert.AreEqual(100f, view.ExtentHeight, 0.5f, "the content reports logical units");
        Assert.AreEqual(10f, view.ViewportHeight, 0.5f);
        Assert.AreEqual(100d, view.VerticalScrollBar!.Maximum, 0.5d);

        view.VerticalOffset = 30f;
        Assert.AreEqual(30d, content.VerticalOffset, 0.5d);
        Assert.AreEqual(30d, view.VerticalScrollBar.Value, 0.5d);

        surface.ProcessPointerWheel(view.PointToSurface(new PointF(50f, 50f)), -120);
        Assert.AreEqual(33d, content.VerticalOffset, 0.5d, "a wheel notch is the content's own step");

        surface.FocusElement(view);
        surface.ProcessKeyDown(Keys.PageDown);
        Assert.AreEqual(43d, content.VerticalOffset, 0.5d, "PageDown uses the content's page step");
    }

    private sealed class LogicalScroller : VisualElement, VC.IScrollInfo
    {
        public bool CanHorizontallyScroll { get; set; }

        public bool CanVerticallyScroll { get; set; } = true;

        public double ExtentWidth => 100d;

        public double ExtentHeight => 100d;

        public double ViewportWidth => 10d;

        public double ViewportHeight => 10d;

        public double HorizontalOffset { get; private set; }

        public double VerticalOffset { get; private set; }

        public VC.ScrollViewer? ScrollOwner { get; set; }

        public void LineUp() => SetVerticalOffset(VerticalOffset - 1d);

        public void LineDown() => SetVerticalOffset(VerticalOffset + 1d);

        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 1d);

        public void LineRight() => SetHorizontalOffset(HorizontalOffset + 1d);

        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

        public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);

        public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 3d);

        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 3d);

        public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 3d);

        public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 3d);

        public void SetHorizontalOffset(double offset) => HorizontalOffset = Math.Clamp(offset, 0d, ExtentWidth - ViewportWidth);

        public void SetVerticalOffset(double offset)
        {
            VerticalOffset = Math.Clamp(offset, 0d, ExtentHeight - ViewportHeight);
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private static VisualHost CreateSurface(int width, int height)
        => new() { Size = new Size(width, height) };

    private static void Layout(VisualHost surface, int width, int height)
    {
        // Auto 滚动条占位会改变 presenter 视口，进而可能让另一条也出现；连排三遍到稳定态。
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        for (var pass = 0; pass < 3; pass++)
        {
            surface.RenderTo(graphics, new Size(width, height));
        }
    }
}
