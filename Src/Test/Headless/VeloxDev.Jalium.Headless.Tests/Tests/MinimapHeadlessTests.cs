using Jalium.UI.Media;
using VeloxDev.AdapterTestKit;
using VeloxDev.Jalium.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.Jalium.Headless.Tests.Tests;

// 小地图的映射、落位与拖拽都在 MinimapPipelineCore；这里通过 Jalium 的宿主层把它驱动一遍，
// 断言与 WPF 同一组数字，失败信息因此能直接指出是哪一家。
[TestClass]
public sealed class MinimapHeadlessTests
{
    [TestMethod]
    public void MinimapPipeline_Drag_MovesTheSurfaceScrollViewer()
    {
        var graph = TestGraph.Create();
        using var surface = new JaliumHeadlessSurface(graph);
        surface.Settle();

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

        surface.Settle();
        Assert.AreEqual(expectedX, surface.Scroll.HorizontalOffset, 1d);
        Assert.AreEqual(expectedY, surface.Scroll.VerticalOffset, 1d);

        var before = surface.Scroll.HorizontalOffset;
        input.PointerPosition = (rect.Left + 60d, rect.Top + 40d);
        pipeline.PointerMoved();
        surface.Settle();

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
