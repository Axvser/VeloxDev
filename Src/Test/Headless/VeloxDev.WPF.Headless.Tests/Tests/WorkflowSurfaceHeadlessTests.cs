using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.AdapterTestKit;
using VeloxDev.WPF.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem.StandardEx;
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

    [TestMethod]
    public void Locate_ScrollsTheNodeToTheViewportCenter()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);
        surface.Layout();

        var node = graph.Far;
        var expectedX = node.Anchor.Horizontal + node.Size.Width / 2d
            - surface.Scroll.ViewportWidth / 2d + graph.Tree.Layout.ActualOffset.Horizontal;
        var expectedY = node.Anchor.Vertical + node.Size.Height / 2d
            - surface.Scroll.ViewportHeight / 2d + graph.Tree.Layout.ActualOffset.Vertical;

        Assert.IsTrue(node.Locate(), "an attached surface registers a navigator");
        WpfHeadlessSurface.Pump();

        Assert.AreEqual(
            Math.Max(0d, expectedX), surface.Scroll.HorizontalOffset, 1d,
            "the far node's center must land at the viewport center");
        Assert.AreEqual(Math.Max(0d, expectedY), surface.Scroll.VerticalOffset, 1d);
    }

    [TestMethod]
    public void Locate_WorksAtAnyZoom()
    {
        foreach (var factor in new[] { 0.5, 2.0 })
        {
            var graph = TestGraph.Create();
            using var surface = new WpfHeadlessSurface(graph);

            // 真实模板里画布的 Width/Height 绑到 Layout.ActualSize；无头夹具是手搭的，这里照同一契约补上，
            // 否则 0.5 倍缩小后 ActualSize 翻倍、夹具仍停在 8000，滚动范围会先把目标夹掉。
            graph.Tree.Layout.OriginSize = new Wf.Size(8000d, 6000d);
            graph.Tree.Layout.Scale = new Wf.Scale(factor, factor);
            Behaviors.WorkflowSurfaceBehavior.Refresh(surface.Host);
            surface.Layout();
            WpfHeadlessSurface.Pump();

            var node = graph.Far;
            var expectedX = node.Anchor.Horizontal + node.Size.Width / 2d
                - surface.Scroll.ViewportWidth / 2d + graph.Tree.Layout.ActualOffset.Horizontal;
            var expectedY = node.Anchor.Vertical + node.Size.Height / 2d
                - surface.Scroll.ViewportHeight / 2d + graph.Tree.Layout.ActualOffset.Vertical;

            Assert.IsTrue(node.Locate(), $"scale {factor}: an attached surface registers a navigator");
            WpfHeadlessSurface.Pump();

            Assert.AreEqual(
                Math.Max(0d, expectedX), surface.Scroll.HorizontalOffset, 1d,
                $"scale {factor}: the node's collapsed center must land at the viewport center");
            Assert.AreEqual(Math.Max(0d, expectedY), surface.Scroll.VerticalOffset, 1d, $"scale {factor}");
        }
    }

    [TestMethod]
    public void WheelOverASiblingControl_DoesNotScrollTheCanvas()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);
        surface.Layout();
        WpfHeadlessSurface.Pump();

        // Put a control next to the surface inside the same host the behaviour is attached to — the shape of
        // the full demo, where the sidebar and the canvas share one UserControl.
        var sidebar = new Button { Content = "side" };
        var root = new DockPanel();
        surface.Host.Content = null;
        root.Children.Add(sidebar);
        root.Children.Add(surface.Border);
        surface.Host.Content = root;
        surface.Layout();
        WpfHeadlessSurface.Pump();

        var before = surface.Scroll.VerticalOffset;
        sidebar.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
            Source = sidebar,
        });
        WpfHeadlessSurface.Pump();

        Assert.AreEqual(
            before, surface.Scroll.VerticalOffset, 1e-9,
            "a wheel over a sibling control must not move the canvas, even when that control has no scroll room");
    }

    [TestMethod]
    public void Locate_GrowsTheCanvasWhenTheElementCannotBeCenteredInIt()
    {
        var graph = TestGraph.Create();
        using var surface = new WpfHeadlessSurface(graph);
        surface.Layout();
        WpfHeadlessSurface.Pump();

        // Before the origin: centering needs a negative scroll, so the canvas must grow instead of clamping to 0.
        Assert.IsTrue(graph.Left.Locate());
        WpfHeadlessSurface.Pump();
        var leftX = graph.Left.Anchor.Horizontal + graph.Left.Size.Width / 2d
            + graph.Tree.Layout.ActualOffset.Horizontal - surface.Scroll.HorizontalOffset;
        Assert.AreEqual(surface.Scroll.ViewportWidth / 2d, leftX, 1d, "negative edge");
        Assert.IsTrue(graph.Tree.Layout.NegativeOffset.Horizontal > 0d, "the canvas grew before the origin");

        // Past the far edge: the node is outside the current extent, so the extent has to grow.
        var edge = new Wf.NodeDefaultViewModel
        {
            Parent = graph.Tree,
            Anchor = new Wf.Anchor(9000, 4000, 0),
            Size = new Wf.Size(160, 90),
        };
        graph.Tree.Nodes.Add(edge);
        surface.Layout();
        WpfHeadlessSurface.Pump();

        Assert.IsTrue(edge.Locate());
        WpfHeadlessSurface.Pump();
        var rightX = edge.Anchor.Horizontal + edge.Size.Width / 2d
            + graph.Tree.Layout.ActualOffset.Horizontal - surface.Scroll.HorizontalOffset;
        Assert.AreEqual(surface.Scroll.ViewportWidth / 2d, rightX, 1d, "positive edge");
        Assert.IsTrue(graph.Tree.Layout.PositiveOffset.Horizontal > 0d, "the canvas grew past the content");
    }
}
