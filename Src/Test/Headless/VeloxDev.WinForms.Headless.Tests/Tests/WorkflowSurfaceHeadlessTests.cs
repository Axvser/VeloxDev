using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem.StandardEx;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// The surface contract mirrors the WPF/Avalonia shape: a plain root control with named parts and the
// surface behavior attached. Refresh does not check IsEnabled; disabling only clears the scroller.
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
    public void ViewPool_CreatesOneViewPerVisibleNode()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var visibleNodes = graph.Tree.Helper.VisibleItems.Count(item => item is IWorkflowNodeViewModel);

        Assert.AreEqual(
            visibleNodes,
            surface.View.Canvas.Controls.Count,
            "links are drawn by WorkflowLinkLayer; the pool only materializes nodes");
        Assert.IsFalse(
            surface.View.Canvas.Controls.OfType<HeadlessLinkView>().Any(),
            "a link must not get a view of its own");
    }

    [TestMethod]
    public void BuiltInSelector_ResolvesTheNodeViewTypeAndTheLinkLayerTakesOverLinks()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph, typedViews: true);

        Assert.IsTrue(
            surface.View.Canvas.Controls.OfType<HeadlessNodeView>().Any(),
            "the named node view type must be materialized by the pool");
        Assert.IsTrue(
            WorkflowLinkLayer.GetIsEnabled(surface.View.Canvas),
            "the surface must enable the single link layer on its canvas");
    }

    [TestMethod]
    public void LinkLayer_PublishesTheCurveWithoutAPerLinkView()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        WorkflowLinkLayer.Refresh(surface.View.Canvas);

        Assert.IsNotNull(
            graph.Link!.HitTarget()?.Curve,
            "the single layer must publish the curve the surface hit-tests against");
        Assert.IsFalse(
            surface.View.Canvas.Controls.OfType<HeadlessLinkView>().Any(),
            "a pooled link view must not be created");
    }

    [TestMethod]
    public void LinkLayer_KeepsTheModelCurveWhenTheSlotMeasurementIsStale()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);
        var canvas = surface.View.Canvas;

        var link = graph.Link!;
        WorkflowLinkLayer.Refresh(canvas);
        var before = link.HitTarget()?.Curve;
        Assert.IsNotNull(before);

        // A pooled endpoint can stop being measured while the projection keeps moving. The layer must
        // keep the model-space curve (and therefore the port's outward direction) and shift it with the pan.
        surface.View.GridDecorator.ScrollOffsetX = -100;
        WorkflowLinkLayer.Refresh(canvas);
        var after = link.HitTarget()?.Curve;
        Assert.IsNotNull(after);

        Assert.AreEqual(before.XAt(0) + 100d, after.XAt(0), 1d);
        Assert.AreEqual(before.YAt(0), after.YAt(0), 1d);
    }

    [TestMethod]
    public void LinkLayer_FollowsANodeAnchorChange()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);
        var link = graph.Link!;

        WorkflowLinkLayer.Refresh(surface.View.Canvas);
        var before = link.HitTarget()?.Curve;
        Assert.IsNotNull(before);

        var anchor = graph.Left.Anchor;
        graph.Left.Anchor = new Anchor(anchor.Horizontal + 50, anchor.Vertical, anchor.Layer);
        WorkflowLinkLayer.Refresh(surface.View.Canvas);
        var after = link.HitTarget()?.Curve;
        Assert.IsNotNull(after);

        Assert.AreEqual(before.XAt(0) + 50d, after.XAt(0), 1d);
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

    [TestMethod]
    public void XamlScrollViewer_ShowsBarsForALargerExtent()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph, xamlScrollViewer: true);

        var viewer = surface.View.XamlScrollViewer!;
        var extent = graph.Tree.Layout.ActualSize;

        Assert.IsTrue(
            extent.Width > viewer.ClientSize.Width && extent.Height > viewer.ClientSize.Height,
            "the default graph extent must exceed the viewport for the bars to matter");
        Assert.IsTrue(viewer.HorizontalScrollBar.Visible, "a wider extent must show the horizontal bar");
        Assert.IsTrue(viewer.VerticalScrollBar.Visible, "a taller extent must show the vertical bar");
        Assert.IsTrue(
            viewer.HorizontalScrollBar.Maximum > 0,
            "the horizontal bar must carry the scrollable range");
    }

    [TestMethod]
    public void XamlScrollViewer_BarValueMovesTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph, xamlScrollViewer: true);

        var viewer = surface.View.XamlScrollViewer!;
        var before = graph.Tree.Helper.Viewport.Vertical;

        viewer.VerticalScrollBar.Value = 200;
        Application.DoEvents();
        WorkflowSurfaceBehavior.Refresh(surface.View);
        Application.DoEvents();

        Assert.AreEqual(
            200d, graph.Tree.Helper.Viewport.Vertical, 2d,
            "the scroll bar offset must become the viewport offset");
        Assert.IsTrue(graph.Tree.Helper.Viewport.Vertical > before, "the viewport must move down");
        Assert.AreEqual(
            200, viewer.ScrollOffset.Y, 1,
            "the synced offset must survive the feedback pass unchanged");
    }

    [TestMethod]
    public void Locate_MovesTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var node = graph.Far;
        var expectedX = node.Anchor.Horizontal + node.Size.Width / 2d - surface.View.ClientSize.Width / 2d;
        var expectedY = node.Anchor.Vertical + node.Size.Height / 2d - surface.View.ClientSize.Height / 2d;

        Assert.IsNotNull(WorkflowInput.For(graph.Tree).Navigator, "an attached surface registers a navigator");
        Assert.IsTrue(node.Locate());
        Application.DoEvents();
        WorkflowSurfaceBehavior.Refresh(surface.View);
        Application.DoEvents();

        Assert.AreEqual(
            System.Math.Max(0d, expectedX), graph.Tree.Helper.Viewport.Horizontal, 2d,
            "the far node's center must land at the viewport center");
        Assert.AreEqual(System.Math.Max(0d, expectedY), graph.Tree.Helper.Viewport.Vertical, 2d);
    }
}
