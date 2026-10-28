using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.AdapterTestKit;
using VeloxDev.WPF.Headless.Tests.Infrastructure;
using Behaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WPF.Headless.Tests.Tests;

/// <summary>
/// Drives the WPF surface without a window: build the visual tree in code, attach the behaviour,
/// lay it out, and assert on the model-side effects the surface is responsible for.
/// </summary>
[STATestClass]
public sealed class WorkflowSurfaceHeadlessTests
{
    [TestMethod]
    public void Attach_ResolvesNamedParts_AndWritesCanvasTransform()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);

        var transform = Behaviors.WorkflowCanvasTransformBehavior.GetTransform(surface.Host);

        Assert.IsInstanceOfType<TranslateTransform>(transform);
        Assert.AreEqual(
            graph.Tree.Layout.ActualOffset.Horizontal,
            ((TranslateTransform)transform!).X,
            1e-9,
            "the surface must publish the layout's actual offset as the canvas transform");
        Assert.AreEqual(
            graph.Tree.Layout.ActualOffset.Horizontal,
            surface.Decorator.ContentOffsetX,
            1e-9,
            "the grid decorator must receive the same content offset");
    }

    [TestMethod]
    public void Refresh_WhileDisabled_IsANoOp()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph, enable: false);

        Behaviors.WorkflowSurfaceBehavior.Refresh(surface.Host);

        Assert.IsNull(Behaviors.WorkflowCanvasTransformBehavior.GetTransform(surface.Host));
        Assert.AreEqual(0d, graph.Tree.Helper.Viewport.Width);
    }

    [TestMethod]
    public void EnablingAfterADisabledRefresh_StartsPublishing()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph, enable: false);

        Behaviors.WorkflowSurfaceBehavior.SetIsEnabled(surface.Host, true);
        Behaviors.WorkflowSurfaceBehavior.Refresh(surface.Host);
        WpfHeadlessSurface.Pump();

        Assert.IsInstanceOfType<TranslateTransform>(
            Behaviors.WorkflowCanvasTransformBehavior.GetTransform(surface.Host));
    }

    [TestMethod]
    public void Scroll_WritesTheViewport_AndPersistsTheOffset()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);

        surface.ScrollTo(120d, 80d);

        var expectedLeft = Wf.WorkflowSurfaceMath.ToWorld(
            120d, graph.Tree.Layout.ActualOffset.Horizontal);
        var expectedTop = Wf.WorkflowSurfaceMath.ToWorld(
            80d, graph.Tree.Layout.ActualOffset.Vertical);

        Assert.AreEqual(expectedLeft, graph.Tree.Helper.Viewport.Horizontal, 0.5);
        Assert.AreEqual(expectedTop, graph.Tree.Helper.Viewport.Vertical, 0.5);
        Assert.AreEqual(
            graph.Tree.Helper.Viewport.Horizontal,
            graph.Tree.Layout.ViewportOffset.Horizontal,
            1e-9,
            "the persisted offset must be written from the same viewport the helper received");
        Assert.AreEqual(
            graph.Tree.Helper.Viewport.Vertical,
            graph.Tree.Layout.ViewportOffset.Vertical,
            1e-9);
    }

    [TestMethod]
    public void ViewportRestore_IsAppliedAfterAttach()
    {
        var graph = TestGraph.Create();
        graph.Tree.Layout.ViewportOffset = new Wf.Offset(150d, 90d);

        using var surface = new WpfHeadlessSurface(graph);
        WpfHeadlessSurface.Pump(DispatcherPriority.ApplicationIdle);

        Assert.AreEqual(150d, surface.Scroll.HorizontalOffset, 1d);
        Assert.AreEqual(90d, surface.Scroll.VerticalOffset, 1d);
        Assert.AreEqual(150d, graph.Tree.Layout.ViewportOffset.Horizontal, 1d);
        Assert.AreEqual(90d, graph.Tree.Layout.ViewportOffset.Vertical, 1d);
    }

    [TestMethod]
    public void Virtualization_ExcludesNodesOutsideTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);

        Assert.IsTrue(graph.Tree.Helper.VisibleItems.Contains(graph.Left));
        Assert.IsTrue(graph.Tree.Helper.VisibleItems.Contains(graph.Right));
        Assert.IsFalse(
            graph.Tree.Helper.VisibleItems.Contains(graph.Far),
            "the far node is outside the 800x600 viewport and must not be visible");

        surface.ScrollTo(6000d, 4000d);

        Assert.IsTrue(
            graph.Tree.Helper.VisibleItems.Contains(graph.Far),
            "scrolling onto the far node must bring it into the visible set");
        Assert.IsFalse(
            graph.Tree.Helper.VisibleItems.Contains(graph.Left),
            "the near nodes must leave the visible set once the viewport moves away");
    }

    [TestMethod]
    public void ViewPool_CreatesOneViewPerVisibleItem()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);

        Behaviors.ViewPool.SetTemplateSelector(surface.Canvas, new AnyTemplateSelector());
        Behaviors.ViewPool.SetItemsSource(surface.Canvas, graph.Tree.Helper.VisibleItems);
        WpfHeadlessSurface.Pump();

        Assert.AreEqual(
            graph.Tree.Helper.VisibleItems.Count,
            surface.Canvas.Children.Count,
            "the pool must materialize exactly one view for each visible item");
    }

    [TestMethod]
    public void Detach_StopsWritingTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);

        surface.ScrollTo(100d, 20d);
        var before = graph.Tree.Helper.Viewport;

        Behaviors.WorkflowSurfaceBehavior.SetIsEnabled(surface.Host, false);
        surface.ScrollTo(400d, 60d);

        Assert.AreEqual(
            before.Horizontal,
            graph.Tree.Helper.Viewport.Horizontal,
            1e-9,
            "after detach the surface must not keep feeding the model");
        Assert.AreEqual(before.Vertical, graph.Tree.Helper.Viewport.Vertical, 1e-9);
    }
}
