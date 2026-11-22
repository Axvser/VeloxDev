using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The reusable workflow surface built on the HWND-free visual backend: view materialisation, published curves,
/// drag, pan/zoom, delete and teardown. Every test drives layout through <see cref="VisualSurface.RenderTo"/>; no
/// message loop is required.
/// </summary>
[STATestClass]
public sealed class VisualWorkflowHostTests
{
    private const int Width = 800;
    private const int Height = 600;

    [TestMethod]
    public void Attach_MaterialisesViewsAndPublishesHitTestableCurves()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);

        var viewA = surface.FindNodeView(model.SenderNode);
        var viewB = surface.FindNodeView(model.ReceiverNode);
        Assert.IsNotNull(viewA);
        Assert.IsNotNull(viewB);

        var curve = model.Link.HitTarget()?.Curve;
        Assert.IsNotNull(curve, "the link layer publishes the curve it draws");
        var (x, y) = curve!.PointAtLength(curve.Length / 2d);
        Assert.IsTrue(model.Link.HitTest(x, y, 6d), "the published curve contains its own midpoint");
        var visible = model.Tree.GetHelper().VisibleItems;
        Assert.IsTrue(
            visible.Contains(model.Link),
            $"link visible={model.Link.IsVisible}; viewport={model.Tree.GetHelper().Viewport}; visible={string.Join(",", visible.Select(i => i.GetType().Name))}; sender={model.Link.Sender?.Anchor}; receiver={model.Link.Receiver?.Anchor}; curve=({x},{y})");
        Assert.AreSame(model.Link, model.Tree.HitTestVisibleLinks(x, y), "the published curve is the hit-testable one");

        var center = surface.Canvas.ModelToSurface(model.SenderNode.Anchor.Horizontal + 10d, model.SenderNode.Anchor.Vertical + 10d);
        Assert.IsTrue(viewA!.AbsoluteBounds.Contains(center), "the card is placed where the model anchor says");
    }

    [TestMethod]
    public void NodeBoundsFollowAnchorAndPan()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);

        var view = surface.FindNodeView(model.SenderNode)!;
        Assert.AreEqual(40d, view.Bounds.X, 1.5d);
        Assert.AreEqual(40d, view.Bounds.Y, 1.5d);

        surface.ScrollBy(0d, 1d);
        Render(surface);
        view = surface.FindNodeView(model.SenderNode)!;
        Assert.AreEqual(40d, view.Bounds.X, 1.5d);
        Assert.AreEqual(40d + VisualWorkflowSurface.WheelScrollStep, view.Bounds.Y, 1.5d);
    }

    [TestMethod]
    public void DraggingNodeMovesTheModel()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);

        var view = surface.FindNodeView(model.SenderNode)!;
        var press = new PointF(view.AbsoluteBounds.X + 20f, view.AbsoluteBounds.Y + 20f);
        var before = model.SenderNode.Anchor.Horizontal;

        surface.ProcessPointerDown(press, MouseButtons.Left);
        surface.ProcessPointerMove(new PointF(press.X + 30f, press.Y + 10f));
        surface.ProcessPointerUp(new PointF(press.X + 30f, press.Y + 10f), MouseButtons.Left);

        Assert.AreEqual(before + 30d, model.SenderNode.Anchor.Horizontal, 1d);
        Assert.AreEqual(50d, model.SenderNode.Anchor.Vertical, 1d);
    }

    [TestMethod]
    public void DeleteKeyRemovesTheHoveredLink()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);

        var curve = model.Link.HitTarget()!.Curve!;
        var (x, y) = curve.PointAtLength(curve.Length / 2d);
        surface.ProcessPointerMove(surface.Canvas.ModelToSurface(x, y));
        Assert.AreSame(model.Link, surface.HoveredLink, "the pointer route resolves the link under the curve");

        surface.FocusSurface();
        surface.ProcessKeyDown(Keys.Delete);

        Assert.AreEqual(0, model.Tree.Links.Count);
    }

    [TestMethod]
    public void WheelScrollAndZoomChangePanAndScale()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);

        var before = surface.Canvas.Pan;
        surface.ScrollBy(0d, 1d);
        Assert.AreEqual(before.Y + VisualWorkflowSurface.WheelScrollStep, surface.Canvas.Pan.Y, 0.01d);

        surface.ZoomAt(new PointF(Width / 2f, Height / 2f), 2d);
        Assert.AreEqual(2d, model.Tree.Layout.Scale.Horizontal, 0.001d);
        Assert.AreNotEqual(0d, surface.Canvas.Pan.X, "zooming keeps the point under the cursor still by moving the pan");
    }

    [TestMethod]
    public void DisposeDetachesTheTreeAndClearsRouteCapabilities()
    {
        var model = CreateModel();
        var surface = CreateSurface(model.Tree);

        surface.Dispose();

        Assert.IsNull(surface.Tree);
        Assert.IsNull(WorkflowInput.For(model.Tree).Scroller);
        Assert.IsNull(WorkflowInput.For(model.Tree).Navigator);
    }

    private static VisualWorkflowSurface CreateSurface(ITreeComponent tree)
    {
        var surface = new VisualWorkflowSurface
        {
            Size = new System.Drawing.Size(Width, Height),
        };
        surface.Tree = tree;
        Render(surface);
        return surface;
    }

    private static void Render(VisualWorkflowSurface surface)
    {
        using var bitmap = new Bitmap(Width, Height);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new System.Drawing.Size(Width, Height));
    }

    private static Model CreateModel()
    {
        var tree = new TreeDefaultComponent
        {
            Layout = { OriginSize = new Wf.Size(1000d, 700d) },
        };
        // LinkHitTestEx walks the virtualised visible set, so the test tree uses the spatial helper the demos use.
        tree.SetHelper(new TreeHelper(200d));

        var helper = tree.GetHelper();
        var sender = new NodeDefaultComponent
        {
            Anchor = new Wf.Anchor(40d, 40d, 0),
            Size = new Wf.Size(120d, 60d),
        };
        var senderSlot = new SlotDefaultComponent { Channel = SlotChannel.OneTarget };
        senderSlot.Parent = sender;
        sender.Slots.Add(senderSlot);
        helper.CreateNode(sender);

        var receiver = new NodeDefaultComponent
        {
            Anchor = new Wf.Anchor(320d, 160d, 0),
            Size = new Wf.Size(120d, 60d),
        };
        var receiverSlot = new SlotDefaultComponent { Channel = SlotChannel.OneSource };
        receiverSlot.Parent = receiver;
        receiver.Slots.Add(receiverSlot);
        helper.CreateNode(receiver);

        helper.SendConnection(senderSlot);
        helper.ReceiveConnection(receiverSlot);

        Assert.AreEqual(1, tree.Links.Count, "the standard connection path creates one link");
        return new Model(tree, sender, receiver, tree.Links[0]);
    }

    private sealed record Model(
        ITreeComponent Tree,
        INodeComponent SenderNode,
        INodeComponent ReceiverNode,
        ILinkComponent Link);
}
