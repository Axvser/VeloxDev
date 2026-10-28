using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// WinForms 的表面契约与 WPF/Avalonia 同形，但刷新不检查 IsEnabled（Host 自己控制生命周期），
// 所以这里不断言「禁用后 Refresh 不写模型」；禁用只清滚动口。
[STATestClass]
public sealed class WorkflowSurfaceHeadlessTests
{
    [TestMethod]
    public void Attach_PublishesTheCanvasTransform()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var transform = WorkflowCanvasTransformBehavior.GetTransform(surface.View);

        Assert.IsNotNull(transform);
        Assert.AreEqual(
            graph.Tree.Layout.ActualOffset.Horizontal,
            transform!.Horizontal,
            1e-9,
            "the surface must publish the layout's content offset as the canvas transform");
    }

    [TestMethod]
    public void Refresh_WritesTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

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
        using var surface = new WinFormsHeadlessSurface(graph);

        var before = graph.Tree.Helper.Viewport.Vertical;
        surface.ScrollBy(0d, -120d);

        Assert.IsTrue(
            graph.Tree.Helper.Viewport.Vertical > before,
            "a wheel notch down must move the viewport down the world");
    }

    [TestMethod]
    public void ViewportRestore_IsAppliedAfterAttach()
    {
        var graph = TestGraph.Create();
        graph.Tree.Layout.ViewportOffset = new Offset(150d, 90d);

        using var surface = new WinFormsHeadlessSurface(graph);
        Application.DoEvents();

        Assert.AreEqual(150d, graph.Tree.Helper.Viewport.Horizontal, 2d);
        Assert.AreEqual(90d, graph.Tree.Helper.Viewport.Vertical, 2d);
    }

    [TestMethod]
    public void Virtualization_ExcludesNodesOutsideTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

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
        using var surface = new WinFormsHeadlessSurface(graph);

        Assert.AreEqual(
            graph.Tree.Helper.VisibleItems.Count,
            surface.View.PART_Canvas.Controls.Count,
            "the pool must materialize exactly one view for each visible item");
    }

    [TestMethod]
    public void Disabling_ClearsTheScroller()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        Assert.IsNotNull(WorkflowInput.For(graph.Tree).Scroller);

        WorkflowSurfaceBehavior.SetIsEnabled(surface.View, false);

        Assert.IsNull(WorkflowInput.For(graph.Tree).Scroller);
    }
}
