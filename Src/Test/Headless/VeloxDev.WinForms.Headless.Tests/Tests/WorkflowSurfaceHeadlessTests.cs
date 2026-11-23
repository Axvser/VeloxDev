using System.Drawing;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using DrawingPointF = System.Drawing.PointF;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// The surface is a single VisualHost whose named parts are visual elements. The camera lives on the surface
// behavior; ViewPool materializes Helper.VisibleItems; input is routed to Core through the visual hit test.
[STATestClass]
public sealed class WorkflowSurfaceHeadlessTests
{
    [TestMethod]
    public void Attach_PublishesTheCameraAndTheDecoratorOffsets()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        Assert.AreEqual(1d, WorkflowSurfaceBehavior.GetZoom(surface.View), 1e-9);
        Assert.AreEqual(
            graph.Tree.Layout.ActualOffset.Horizontal,
            surface.View.GridDecorator.ContentOffsetX,
            1e-9,
            "the decorator must receive the same content offset the camera uses");
        Assert.AreEqual(
            graph.Tree.Layout.ActualOffset.Vertical,
            surface.View.GridDecorator.ContentOffsetY,
            1e-9);
    }

    [TestMethod]
    public void Zoom_KeepsTheWorldPointUnderThePointer()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);
        var point = new PointF(320f, 240f);

        var before = WorkflowSurfaceBehavior.SurfaceToWorld(surface.View, point);
        WorkflowSurfaceBehavior.ZoomAt(surface.View, point, 2d);
        var after = WorkflowSurfaceBehavior.SurfaceToWorld(surface.View, point);

        Assert.AreEqual(2d, WorkflowSurfaceBehavior.GetZoom(surface.View), 1e-6);
        Assert.AreEqual(before.X, after.X, 1e-3, "the point under the cursor must stay fixed");
        Assert.AreEqual(before.Y, after.Y, 1e-3);
    }

    [TestMethod]
    public void WorldToSurface_RoundTripsThroughSurfaceToWorld()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var world = new PointF(123.5f, 67.25f);
        var surfacePoint = WorkflowSurfaceBehavior.WorldToSurface(surface.View, world);
        var back = WorkflowSurfaceBehavior.SurfaceToWorld(surface.View, surfacePoint);

        Assert.AreEqual(world.X, back.X, 1e-3);
        Assert.AreEqual(world.Y, back.Y, 1e-3);
    }

    [TestMethod]
    public void SetWorldViewport_RoundTripsThroughTheHelper()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        WorkflowSurfaceBehavior.SetWorldViewport(surface.View, new Offset(150d, 90d));

        var world = WorkflowSurfaceBehavior.GetWorldViewport(surface.View);
        Assert.AreEqual(150d, world.Horizontal, 1e-3);
        Assert.AreEqual(90d, world.Vertical, 1e-3);
        Assert.AreEqual(150d, graph.Tree.Helper.Viewport.Horizontal, 1e-3);
        Assert.AreEqual(90d, graph.Tree.Helper.Viewport.Vertical, 1e-3);
    }

    [TestMethod]
    public void SetPan_IsReportedByTheCamera()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        WorkflowSurfaceBehavior.SetPan(surface.View, new PointF(24f, -36f));

        var pan = WorkflowSurfaceBehavior.GetPan(surface.View);
        Assert.AreEqual(24f, pan.X, 1e-3);
        Assert.AreEqual(-36f, pan.Y, 1e-3);
        Assert.AreEqual(-24d, WorkflowSurfaceBehavior.GetWorldViewport(surface.View).Horizontal, 1e-3);
    }

    [TestMethod]
    public void Refresh_WritesTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        WorkflowSurfaceBehavior.Refresh(surface.View);

        Assert.IsTrue(graph.Tree.Helper.Viewport.Width > 0d);
        Assert.IsTrue(graph.Tree.Helper.Viewport.Height > 0d);
        var world = WorkflowSurfaceBehavior.GetWorldViewport(surface.View);
        Assert.AreEqual(world.Horizontal, graph.Tree.Helper.Viewport.Horizontal, 1e-9);
        Assert.AreEqual(world.Vertical, graph.Tree.Helper.Viewport.Vertical, 1e-9);
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
    public void Disabling_ClearsTheScroller()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        Assert.IsNotNull(WorkflowInput.For(graph.Tree).Scroller);

        WorkflowSurfaceBehavior.SetIsEnabled(surface.View, false);

        Assert.IsNull(WorkflowInput.For(graph.Tree).Scroller);
    }

    [TestMethod]
    public void Disabling_PersistsTheWorldViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        WorkflowSurfaceBehavior.SetWorldViewport(surface.View, new Offset(120d, 80d));
        WorkflowSurfaceBehavior.SetIsEnabled(surface.View, false);

        Assert.AreEqual(120d, graph.Tree.Layout.ViewportOffset.Horizontal, 1e-3);
        Assert.AreEqual(80d, graph.Tree.Layout.ViewportOffset.Vertical, 1e-3);
    }

    [TestMethod]
    public void ViewportRestore_IsAppliedAfterAttach()
    {
        var graph = TestGraph.Create();
        graph.Tree.Layout.ViewportOffset = new Offset(150d, 90d);

        using var surface = new WinFormsHeadlessSurface(graph);
        surface.Render();

        var world = WorkflowSurfaceBehavior.GetWorldViewport(surface.View);
        Assert.AreEqual(150d, world.Horizontal, 1d);
        Assert.AreEqual(90d, world.Vertical, 1d);
        Assert.AreEqual(150d, graph.Tree.Helper.Viewport.Horizontal, 1d);
        Assert.AreEqual(90d, graph.Tree.Helper.Viewport.Vertical, 1d);
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

        WorkflowSurfaceBehavior.SetWorldViewport(surface.View, new Offset(6000d, 4000d));
        surface.Render();

        Assert.IsTrue(
            graph.Tree.Helper.VisibleItems.Contains(graph.Far),
            "scrolling onto the far node must bring it into the visible set");
        Assert.IsFalse(
            graph.Tree.Helper.VisibleItems.Contains(graph.Left),
            "the near nodes must leave the visible set once the viewport moves away");
    }

    [TestMethod]
    public void ViewPool_MaterializesExactlyTheVisibleItems()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var visible = graph.Tree.GetHelper().VisibleItems;
        Assert.IsTrue(visible.Count > 0);
        Assert.AreEqual(
            visible.Count,
            surface.View.Canvas.Children.Count,
            "the pool must materialize exactly one view for each visible item");

        var nodeView = surface.View.ViewFor(graph.Left);
        Assert.IsNotNull(nodeView, "the left node must have a pooled view");
        Assert.AreSame(graph.Left, nodeView!.DataContext);

        var linkView = surface.View.ViewFor(graph.Link!);
        Assert.IsNotNull(linkView, "the link must have its own pooled view");
        Assert.IsTrue(
            surface.View.Canvas.Children.IndexOf(linkView!) < surface.View.Canvas.Children.IndexOf(nodeView),
            "links sit below the node cards");
    }


    [TestMethod]
    public void LinkView_KeepsTheCanvasSizeAfterANodeMoves()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);
        var linkView = surface.View.ViewFor(graph.Link!);
        Assert.IsNotNull(linkView);
        Assert.AreEqual(surface.View.Canvas.Bounds.Width, linkView!.Bounds.Width, 1d,
            "the link uses the canvas width, not a fixed box");

        var anchor = graph.Left.Anchor;
        graph.Left.Anchor = new Anchor(anchor.Horizontal + 80d, anchor.Vertical + 40d, anchor.Layer);
        surface.Render();

        Assert.AreSame(graph.Link, linkView.DataContext, "moving a node keeps the pooled link view");
        Assert.AreEqual(surface.View.Canvas.Bounds.Width, linkView.Bounds.Width, 1d,
            "the link keeps the canvas width after a node move");
        Assert.IsTrue(linkView.Visibility == VeloxDev.WinForms.Xaml.Controls.Visibility.Visible);
    }

    [TestMethod]
    public void LinkView_PublishesTheCurveTheSurfaceHitTests()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);
        var link = graph.Link!;

        var curve = link.HitTarget()?.Curve;
        Assert.IsNotNull(curve, "the per-link view must publish the curve it drew");

        Assert.AreEqual(graph.LeftOutput.Anchor.Horizontal, curve!.XAt(0), 1d);
        Assert.AreEqual(graph.LeftOutput.Anchor.Vertical, curve.YAt(0), 1d);
        Assert.AreEqual(graph.RightInput.Anchor.Horizontal, curve.XAt(curve.Count - 1), 1d);
        Assert.AreEqual(graph.RightInput.Anchor.Vertical, curve.YAt(curve.Count - 1), 1d);

        var mid = curve.PointAtLength(curve.Length / 2d);
        Assert.AreSame(
            link,
            graph.Tree.HitTestVisibleLinks(mid.X, mid.Y),
            "a point on the published curve must resolve to that link");
    }

    [TestMethod]
    public void LinkCurve_FollowsANodeAnchorChange()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);
        var link = graph.Link!;

        var before = link.HitTarget()?.Curve;
        Assert.IsNotNull(before);

        var anchor = graph.Left.Anchor;
        graph.Left.Anchor = new Anchor(anchor.Horizontal + 50d, anchor.Vertical, anchor.Layer);

        // The node view moves through its binding; the slot anchors are re-measured during layout, which also
        // republishes the curves.
        surface.Render();

        var after = link.HitTarget()?.Curve;
        Assert.IsNotNull(after);
        Assert.AreEqual(before!.XAt(0) + 50d, after!.XAt(0), 1d);
        Assert.AreEqual(before.YAt(0), after.YAt(0), 1d);
    }


    [TestMethod]
    public void HorizontalWheel_MovesXNotY_AndShiftWheelUsesTheSameAxis()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);
        var point = new DrawingPointF(400f, 300f);
        var before = WorkflowSurfaceBehavior.GetPan(surface.View);

        surface.View.ProcessPointerHWheel(point, -120);
        surface.Render();
        var afterHorizontal = WorkflowSurfaceBehavior.GetPan(surface.View);
        Assert.AreNotEqual(before.X, afterHorizontal.X, "a horizontal wheel moves the camera horizontally");
        Assert.AreEqual(before.Y, afterHorizontal.Y, 1d, "a horizontal wheel does not move vertically");

        WorkflowSurfaceBehavior.ProcessWheel(surface.View, point, 120, Keys.Shift);
        surface.Render();
        var afterShift = WorkflowSurfaceBehavior.GetPan(surface.View);
        Assert.AreNotEqual(afterHorizontal.X, afterShift.X, "Shift+wheel moves the same horizontal axis");
        Assert.AreEqual(afterHorizontal.Y, afterShift.Y, 1d);
    }

    [TestMethod]
    public void CorePressAndRelease_ReachTheNodeUnderThePointer()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var leftCenter = NodeCenter(graph.Left);
        surface.Press(surface.WorldToSurface(leftCenter));
        Assert.AreSame(graph.Left, WorkflowInput.For(graph.Tree).PointerTarget,
            "the press must reach Core with the node under it");

        var rightCenter = NodeCenter(graph.Right);
        surface.Release(surface.WorldToSurface(rightCenter));
        Assert.AreSame(graph.Right, WorkflowInput.For(graph.Tree).PointerTarget,
            "the release must be resolved at its own position, not the press's");
    }

    [TestMethod]
    public void Locate_MovesTheViewport()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph);

        var node = graph.Far;
        var client = surface.View.LogicalClientSize;
        var expectedX = node.Anchor.Horizontal + (node.Size.Width / 2d) - (client.Width / 2d);
        var expectedY = node.Anchor.Vertical + (node.Size.Height / 2d) - (client.Height / 2d);

        Assert.IsNotNull(WorkflowInput.For(graph.Tree).Navigator, "an attached surface registers a navigator");
        Assert.IsTrue(node.Locate());
        surface.Render();

        var world = WorkflowSurfaceBehavior.GetWorldViewport(surface.View);
        Assert.AreEqual(expectedX, world.Horizontal, 1d,
            "the far node's center must land at the viewport center");
        Assert.AreEqual(expectedY, world.Vertical, 1d);
        Assert.AreEqual(expectedX, graph.Tree.Helper.Viewport.Horizontal, 1d);
        Assert.AreEqual(expectedY, graph.Tree.Helper.Viewport.Vertical, 1d);
    }

    private static PointF NodeCenter(INodeComponent node)
        => new(
            (float)(node.Anchor.Horizontal + (node.Size.Width / 2d)),
            (float)(node.Anchor.Vertical + (node.Size.Height / 2d)));
}
