using System.Drawing;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// The minimap is a full-surface sibling layer. Tree subscription, mapping, placement and drag math live in the
// Core pipeline; the surface pushes canvas-local (world) offsets and listens for ViewportScrollRequested.
[STATestClass]
public sealed class MinimapHeadlessTests
{
    [TestMethod]
    public void Minimap_ReceivesTheWorldViewportAndTheAvailableCorner()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph, withMinimap: true);
        surface.Render();
        var minimap = surface.Minimap!;

        var world = WorkflowSurfaceBehavior.GetWorldViewport(surface.View);
        Assert.AreEqual(world.Horizontal, minimap.ScrollOffsetX, 1e-3,
            "the minimap scroll offset is the canvas-local viewport origin, not surface pixels");
        Assert.AreEqual(world.Vertical, minimap.ScrollOffsetY, 1e-3);
        Assert.AreEqual(0d, minimap.ContentOffsetX, 1e-9);
        Assert.AreEqual(0d, minimap.ContentOffsetY, 1e-9);
        Assert.IsTrue(minimap.ViewportWidth > 0d, "the surface must push its visible viewport size");
        Assert.IsTrue(minimap.ViewportHeight > 0d);
        Assert.IsTrue(minimap.AvailableRight > 0d, "the available top-right corner must be resolved");
        Assert.IsTrue(minimap.AvailableBottom > minimap.AvailableTop);
    }

    [TestMethod]
    public void MinimapDrag_RaisesTheScrollRequestAndMovesTheCamera()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph, withMinimap: true);
        surface.Render();
        var minimap = surface.Minimap!;

        double requestedX = double.NaN;
        double requestedY = double.NaN;
        minimap.ViewportScrollRequested += (x, y) =>
        {
            requestedX = x;
            requestedY = y;
        };

        var before = WorkflowSurfaceBehavior.GetWorldViewport(surface.View);

        // The placed minimap sits at the available top-right corner: its top-left is (right - margin - width,
        // top + margin). Press at its centre, then drag left.
        var press = new PointF(
            (float)(minimap.AvailableRight - minimap.MinimapMargin - (minimap.MinimapWidth / 2d)),
            (float)(minimap.AvailableTop + minimap.MinimapMargin + (minimap.MinimapHeight / 2d)));
        var drag = new PointF(press.X - 60f, press.Y);

        Assert.IsTrue(surface.Press(press), "the press must land on the minimap overlay");
        surface.MoveTo(drag);
        surface.Release(drag);

        // 相机按 60Hz 合并位移；无头宿主没有原生 MouseUp，这里补一拍冲掉待应用的最后一帧。
        surface.View.RaiseHostMouseUp((int)drag.X, (int)drag.Y);
        surface.Render();

        Assert.IsFalse(double.IsNaN(requestedX), "dragging the viewport must raise ViewportScrollRequested");
        Assert.IsFalse(double.IsNaN(requestedY));

        var after = WorkflowSurfaceBehavior.GetWorldViewport(surface.View);
        Assert.AreNotEqual(before.Horizontal, after.Horizontal, 1e-3,
            "the drag must move the surface viewport horizontally");
        Assert.AreEqual(after.Horizontal, graph.Tree.Helper.Viewport.Horizontal, 1e-3,
            "the pushed tree viewport follows the camera");
    }
}
