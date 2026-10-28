using Jalium.UI.Media;
using VeloxDev.AdapterTestKit;
using VeloxDev.Jalium.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Jalium.Headless.Tests.Tests;

// Jalium 表面与 WPF 同形（附着行为 + 名字认领）；这里的断言也保持同形，
// 失败信息因此能直接指出是哪一家。
[TestClass]
public sealed class WorkflowSurfaceHeadlessTests
{
    [TestMethod]
    public void Attach_PublishesTheCanvasTransform()
    {
        var graph = TestGraph.Create();
        using var surface = new JaliumHeadlessSurface(graph);

        var transform = WorkflowSurfaceBehavior.GetCanvasTransform(surface.Host);

        Assert.IsInstanceOfType<TranslateTransform>(transform);
        Assert.AreEqual(
            graph.Tree.Layout.ActualOffset.Horizontal,
            ((TranslateTransform)transform!).X,
            1e-9,
            "the surface must publish the layout's content offset as the canvas transform");
    }

    [TestMethod]
    public void Refresh_WritesTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new JaliumHeadlessSurface(graph);

        Assert.IsTrue(graph.Tree.Helper.Viewport.Width > 0d);
        Assert.IsTrue(graph.Tree.Helper.Viewport.Height > 0d);
        Assert.AreEqual(
            graph.Tree.Helper.Viewport.Horizontal,
            graph.Tree.Layout.ViewportOffset.Horizontal,
            1e-9,
            "the persisted offset must be written from the same viewport the helper received");
    }

    [TestMethod]
    public void Scroll_WritesTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new JaliumHeadlessSurface(graph);

        surface.ScrollTo(120d, 80d);

        Assert.AreEqual(120d, graph.Tree.Helper.Viewport.Horizontal, 1d);
        Assert.AreEqual(80d, graph.Tree.Helper.Viewport.Vertical, 1d);
    }

    [TestMethod]
    public void ViewportRestore_IsAppliedAfterAttach()
    {
        var graph = TestGraph.Create();
        graph.Tree.Layout.ViewportOffset = new Offset(150d, 90d);

        using var surface = new JaliumHeadlessSurface(graph);
        JaliumHeadlessSurface.Pump();

        Assert.AreEqual(150d, surface.Scroll.HorizontalOffset, 1d);
        Assert.AreEqual(90d, surface.Scroll.VerticalOffset, 1d);
    }

    [TestMethod]
    public void Virtualization_ExcludesNodesOutsideTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new JaliumHeadlessSurface(graph);

        Assert.IsTrue(graph.Tree.Helper.VisibleItems.Contains(graph.Left));
        Assert.IsTrue(graph.Tree.Helper.VisibleItems.Contains(graph.Right));
        Assert.IsFalse(
            graph.Tree.Helper.VisibleItems.Contains(graph.Far),
            "the far node is outside the 800x600 viewport and must not be visible");
    }

    [TestMethod]
    public void ViewPool_CreatesOneViewPerVisibleItem()
    {
        var graph = TestGraph.Create();
        using var surface = new JaliumHeadlessSurface(graph);

        Assert.AreEqual(
            graph.Tree.Helper.VisibleItems.Count,
            surface.Canvas.Children.Count,
            "the pool must materialize exactly one view for each visible item");
    }
}
