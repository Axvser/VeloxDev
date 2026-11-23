using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The visual surface camera: the canvas transform scales the pooled node views, and pointer coordinates map back
/// through its inverse so hit testing and gestures follow the zoom.
/// </summary>
[STATestClass]
public sealed class WorkflowCameraTests
{
    [TestMethod]
    [DataRow(0.68d, DisplayName = "68%")]
    [DataRow(1.46d, DisplayName = "146%")]
    public void ZoomAtKeepsTheWorldPointUnderThePointer(double zoom)
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        var pointer = new PointF(333f, 211f);
        var worldBefore = WorkflowSurfaceBehavior.SurfaceToWorld(fixture.Host, pointer);

        WorkflowSurfaceBehavior.ZoomAt(fixture.Host, pointer, zoom);

        Assert.AreEqual(zoom, WorkflowSurfaceBehavior.GetZoom(fixture.Host), 0.0001d);
        var worldAfter = WorkflowSurfaceBehavior.SurfaceToWorld(fixture.Host, pointer);
        Assert.AreEqual(worldBefore.X, worldAfter.X, 0.05d, "the world point under the pointer stays fixed");
        Assert.AreEqual(worldBefore.Y, worldAfter.Y, 0.05d);
    }

    [TestMethod]
    [DataRow(0.25d, DisplayName = "25%")]
    [DataRow(0.68d, DisplayName = "68%")]
    [DataRow(1d, DisplayName = "100%")]
    [DataRow(1.46d, DisplayName = "146%")]
    public void SurfaceToWorldRoundTripsWorldToSurface(double zoom)
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        if (Math.Abs(zoom - 1d) > 0.0001d)
        {
            WorkflowSurfaceBehavior.ZoomAt(fixture.Host, new PointF(200f, 150f), zoom);
        }

        var world = new PointF(137.5f, 92.25f);
        var surface = WorkflowSurfaceBehavior.WorldToSurface(fixture.Host, world);
        var roundTrip = WorkflowSurfaceBehavior.SurfaceToWorld(fixture.Host, surface);

        Assert.AreEqual(world.X, roundTrip.X, 0.01d);
        Assert.AreEqual(world.Y, roundTrip.Y, 0.01d);
    }

    [TestMethod]
    [DataRow(0.68d, DisplayName = "68%")]
    [DataRow(1.46d, DisplayName = "146%")]
    public void NodeTemplateBindingsProduceBoundsThroughTheCamera(double zoom)
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        var node = WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        WorkflowSurfaceBehavior.ZoomAt(fixture.Host, new PointF(0f, 0f), zoom);
        fixture.Layout();

        var view = fixture.NodeView(node);
        Assert.AreEqual(100f, VC.Canvas.GetLeft(view), 0.01d, "Canvas.Left binds Anchor.Horizontal");
        Assert.AreEqual(80f, VC.Canvas.GetTop(view), 0.01d, "Canvas.Top binds Anchor.Vertical");
        Assert.AreEqual(0, VC.Panel.GetZIndex(view), "Panel.ZIndex binds Anchor.Layer");

        Assert.AreEqual(100f, view.Bounds.X, 0.5f, "world bounds stay in canvas coordinates");
        Assert.AreEqual(80f, view.Bounds.Y, 0.5f);
        Assert.AreEqual(200f, view.Bounds.Width, 0.5f, "Width binds Size.Width");
        Assert.AreEqual(120f, view.Bounds.Height, 0.5f, "Height binds Size.Height");

        var absolute = view.AbsoluteBounds;
        var expected = WorkflowSurfaceBehavior.WorldToSurface(fixture.Host, new PointF(100f, 80f));
        Assert.AreEqual(expected.X, absolute.X, 1f, "the camera maps the bound origin to the surface");
        Assert.AreEqual(expected.Y, absolute.Y, 1f);
        Assert.AreEqual(200f * zoom, absolute.Width, 1f, "the camera scales the bound width");
        Assert.AreEqual(120f * zoom, absolute.Height, 1f);
    }

    [TestMethod]
    [DataRow(0.68d, DisplayName = "68%")]
    [DataRow(1.46d, DisplayName = "146%")]
    public void HitTestingUsesTheCameraTransform(double zoom)
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        var node = WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d, SlotChannel.OneSource);
        fixture.Layout();

        Assert.IsFalse(double.IsNaN(node.Slots[0].Anchor.Horizontal), "the slot anchor is measured before hit testing");

        WorkflowSurfaceBehavior.ZoomAt(fixture.Host, new PointF(123f, 77f), zoom);
        fixture.Layout();

        var cardCenter = fixture.ToSurface(200f, 140f);
        var hit = fixture.Host.FindElementAt(cardCenter);
        Assert.IsNotNull(hit, "a point inside the card is a hit after the camera scaled it");
        Assert.AreSame(node, hit.DataContext, "the hit element belongs to the node under the pointer");

        var slot = node.Slots[0];
        var slotPoint = fixture.ToSurface(slot.Anchor.Horizontal, slot.Anchor.Vertical);
        var slotHit = fixture.Host.FindElementAt(slotPoint);
        Assert.IsNotNull(slotHit, "the slot glyph is hittable through the camera");
        Assert.AreSame(slot, slotHit.DataContext);

        var outside = fixture.ToSurface(40f, 40f);
        var outsideHit = fixture.Host.FindElementAt(outside);
        Assert.IsFalse(ReferenceEquals(outsideHit?.DataContext, node), "a world point beyond the card is not a hit");
    }

    [TestMethod]
    public void ControlWheelZoomsTheCamera()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        Assert.AreEqual(1d, WorkflowSurfaceBehavior.GetZoom(fixture.Host), 0.0001d);

        Assert.IsTrue(WorkflowSurfaceBehavior.ProcessWheel(
            fixture.Host, new PointF(200f, 150f), 120, Keys.Control));
        Assert.AreEqual(1.1d, WorkflowSurfaceBehavior.GetZoom(fixture.Host), 0.0001d,
            "one wheel notch up zooms in");

        Assert.IsTrue(WorkflowSurfaceBehavior.ProcessWheel(
            fixture.Host, new PointF(200f, 150f), -120, Keys.Control));
        Assert.AreEqual(1d, WorkflowSurfaceBehavior.GetZoom(fixture.Host), 0.0001d,
            "one wheel notch down zooms back out");
    }

    [TestMethod]
    public void WorldViewportSurvivesZoomPanAndRestore()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        WorkflowSurfaceBehavior.ZoomAt(fixture.Host, new PointF(150f, 100f), 2d);
        WorkflowSurfaceBehavior.SetPan(fixture.Host, new PointF(-40f, 25f));

        var saved = WorkflowSurfaceBehavior.GetWorldViewport(fixture.Host);
        WorkflowSurfaceBehavior.SetWorldViewport(
            fixture.Host,
            new Offset(saved.Horizontal + 90d, saved.Vertical + 60d));
        WorkflowSurfaceBehavior.SetWorldViewport(fixture.Host, saved);

        var restored = WorkflowSurfaceBehavior.GetWorldViewport(fixture.Host);
        Assert.AreEqual(saved.Horizontal, restored.Horizontal, 0.05d);
        Assert.AreEqual(saved.Vertical, restored.Vertical, 0.05d);
        Assert.AreEqual(2d, WorkflowSurfaceBehavior.GetZoom(fixture.Host), 0.0001d,
            "restoring the viewport keeps the zoom");
    }

    [TestMethod]
    public void CanvasTransformCarriesTheCameraMatrix()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        WorkflowSurfaceBehavior.ZoomAt(fixture.Host, new PointF(0f, 0f), 0.68d);
        WorkflowSurfaceBehavior.SetPan(fixture.Host, new PointF(12f, -7f));

        var transform = fixture.Canvas.Transform;
        Assert.AreEqual(0.68f, transform.Elements[0], 0.0001f);
        Assert.AreEqual(0f, transform.Elements[1], 0.0001f);
        Assert.AreEqual(0f, transform.Elements[2], 0.0001f);
        Assert.AreEqual(0.68f, transform.Elements[3], 0.0001f);

        var worldOrigin = WorkflowSurfaceBehavior.WorldToSurface(fixture.Host, PointF.Empty);
        Assert.AreEqual(worldOrigin.X, transform.Elements[4], 0.01f,
            "the canvas translation is the surface point of the world origin");
        Assert.AreEqual(worldOrigin.Y, transform.Elements[5], 0.01f);
    }
}
