using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VeloxDev.AdapterTestKit;
using VeloxDev.WPF.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WPF.Headless.Tests.Tests;

// 小地图是自绘控件：量好、排好、用 RenderTargetBitmap 走一次 OnRender，
// 就能在无窗口进程里覆盖绘制分支（不需要真实呈现源）。
[STATestClass]
public sealed class MinimapHeadlessTests
{
    [TestMethod]
    public void Minimap_RendersNodesAndViewport()
    {
        var graph = TestGraph.Create();
        var minimap = new WorkflowMinimapOverlay
        {
            Width = 200d,
            Height = 140d,
            WorkflowTree = graph.Tree,
            ViewportWidth = 800d,
            ViewportHeight = 600d,
            IsMinimapVisible = true,
        };

        minimap.Measure(new System.Windows.Size(200d, 140d));
        minimap.Arrange(new Rect(0d, 0d, 200d, 140d));

        var bitmap = new RenderTargetBitmap(200, 140, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(minimap);

        Assert.AreEqual(200, bitmap.PixelWidth);
        Assert.AreEqual(140, bitmap.PixelHeight);
    }

    [TestMethod]
    public void Minimap_Hidden_StillMeasures()
    {
        var graph = TestGraph.Create();
        var minimap = new WorkflowMinimapOverlay
        {
            Width = 200d,
            Height = 140d,
            WorkflowTree = graph.Tree,
            IsMinimapVisible = false,
        };

        minimap.Measure(new System.Windows.Size(200d, 140d));
        minimap.Arrange(new Rect(0d, 0d, 200d, 140d));

        var bitmap = new RenderTargetBitmap(200, 140, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(minimap);

        Assert.AreEqual(200, bitmap.PixelWidth);
    }

    [TestMethod]
    public void MinimapPipeline_Drag_MovesTheSurfaceScrollViewer()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);
        surface.Layout();

        var viewport = new MinimapViewportHost(surface.Scroll);
        var input = new MinimapInputHost();
        using var pipeline = new MinimapPipeline(
            new NullRenderer(),
            viewport,
            input,
            new MinimapMetrics
            {
                Width = 200d,
                Height = 140d,
                MinSize = 20d,
                Padding = 2d,
                NodeCornerRadius = 1d,
                MinimapCornerRadius = 4d,
                MinimumThumbSize = 2d,
                Available = new SurfaceAvailableArea
                {
                    Right = 800d,
                    Top = 0d,
                    Bottom = 600d,
                    Margin = 12d,
                },
            })
        {
            Tree = graph.Tree,
        };

        pipeline.Refresh();
        var context = pipeline.DrawContext!;
        var rect = pipeline.MinimapRect;
        Assert.AreEqual(588d, rect.Left, 1e-9);
        Assert.AreEqual(12d, rect.Top, 1e-9);

        var (expectedX, expectedY) = ExpectedScroll(context, rect, 180d, 120d, viewport);

        input.PointerPosition = (rect.Left + 180d, rect.Top + 120d);
        input.IsPressed = true;

        Assert.IsTrue(pipeline.PointerPressed());
        Assert.IsTrue(pipeline.IsDragging);

        surface.Scroll.UpdateLayout();
        Assert.AreEqual(expectedX, surface.Scroll.HorizontalOffset, 1d);
        Assert.AreEqual(expectedY, surface.Scroll.VerticalOffset, 1d);

        var before = surface.Scroll.HorizontalOffset;
        input.PointerPosition = (rect.Left + 60d, rect.Top + 40d);
        pipeline.PointerMoved();
        surface.Scroll.UpdateLayout();

        Assert.AreNotEqual(before, surface.Scroll.HorizontalOffset, "dragging to another point must scroll again");

        pipeline.PointerReleased();
        Assert.IsFalse(pipeline.IsDragging);
    }

    private static (double X, double Y) ExpectedScroll(
        MinimapDrawContext context,
        WorkflowBounds rect,
        double minimapX,
        double minimapY,
        IMinimapViewportHost viewport)
    {
        var pad = context.Padding;
        var (ox, oy, scale) = WorkflowSurfaceMath.MinimapFit(
            context.ContentBounds.Width,
            context.ContentBounds.Height,
            rect.Width - pad * 2,
            rect.Height - pad * 2,
            pad);
        var (canvasX, canvasY) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            minimapX, minimapY, ox, oy, scale, context.ContentBounds.Left, context.ContentBounds.Top);
        return WorkflowSurfaceMath.MinimapToScroll(
            canvasX, canvasY, viewport.ViewportWidth, viewport.ViewportHeight,
            viewport.ContentOffsetX, viewport.ContentOffsetY);
    }

    private sealed class NullRenderer : IMinimapRenderer<DrawingContext>
    {
        public void Render(DrawingContext surface, MinimapDrawContext context)
        {
        }
    }
}
