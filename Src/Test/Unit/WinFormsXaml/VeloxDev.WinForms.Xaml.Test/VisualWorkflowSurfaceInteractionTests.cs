using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WinForms.Xaml.Test;

[STATestClass]
public sealed class VisualWorkflowSurfaceInteractionTests
{
    private const int Width = 800;
    private const int Height = 600;

    [TestMethod]
    public void MinimapPressRecentresTheViewport()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);
        var map = surface.Minimap;
        var bounds = map.AbsoluteBounds;
        var center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
        var before = surface.Canvas.Pan;

        surface.ProcessPointerDown(center, MouseButtons.Left);

        Assert.AreNotEqual(before, surface.Canvas.Pan, "a minimap click centres the pressed content point");
    }

    [TestMethod]
    public void MinimapDragKeepsFollowingThePointer()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);
        var bounds = surface.Minimap.AbsoluteBounds;
        var center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));

        surface.ProcessPointerDown(center, MouseButtons.Left);
        var pressed = surface.Canvas.Pan;
        surface.ProcessPointerMove(new PointF(center.X + 24f, center.Y + 12f));

        Assert.AreNotEqual(pressed, surface.Canvas.Pan, "dragging the minimap recentres on the new pointer position");
    }

    [TestMethod]
    public void DraggingNodeAtZoomFollowsThePointer()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);
        model.Tree.Layout.Scale = new Wf.Scale(2d, 2d);
        surface.Refresh();
        Render(surface);

        var view = surface.FindNodeView(model.SenderNode)!;
        var press = new PointF(view.AbsoluteBounds.X + 20f, view.AbsoluteBounds.Y + 20f);
        var beforeX = model.SenderNode.Anchor.Horizontal;
        var beforeY = model.SenderNode.Anchor.Vertical;

        surface.ProcessPointerDown(press, MouseButtons.Left);
        surface.ProcessPointerMove(new PointF(press.X + 30f, press.Y + 10f));
        surface.ProcessPointerUp(new PointF(press.X + 30f, press.Y + 10f), MouseButtons.Left);

        Assert.AreEqual(beforeX + 30d, model.SenderNode.Anchor.Horizontal, 1d, "the card follows the pointer at zoom");
        Assert.AreEqual(beforeY + 10d, model.SenderNode.Anchor.Vertical, 1d);
    }

    [TestMethod]
    public void TemplateSelectorBuildsTheSlotGlyphs()
    {
        var model = CreateModel();
        using var surface = new VisualWorkflowSurface
        {
            Size = new System.Drawing.Size(Width, Height),
            TemplateSelector = new NodeAndSlotSelector(),
        };
        surface.Tree = model.Tree;
        Render(surface);

        var view = surface.FindNodeView(model.SenderNode)!;
        var slotViews = VisualWorkflowNodeBehavior.GetSlotViews(view);
        Assert.IsTrue(slotViews.Count > 0);
        Assert.IsTrue(slotViews.All(slotView => slotView is TestSlotView), "the slot template is used for every glyph");
    }

    [TestMethod]
    public void NodeContentScalesWithTheCollapsedCard()
    {
        var model = CreateModel();
        using var surface = new VisualWorkflowSurface
        {
            Size = new System.Drawing.Size(Width, Height),
            TemplateSelector = new ProbeSelector(),
        };
        surface.Tree = model.Tree;
        Render(surface);
        var probe = ((ProbeNodeView)surface.FindNodeView(model.SenderNode)!).Probe;

        Assert.AreEqual(1f, probe.RenderedScale, 0.01f, "at scale one the design box is drawn 1:1");

        model.Tree.Layout.Scale = new Wf.Scale(2d, 2d);
        surface.Refresh();
        Render(surface);

        Assert.AreEqual(0.5f, probe.RenderedScale, 0.01f, "the interior follows the collapsed card");
    }

    [TestMethod]
    public void RulerLeftBandStaysAtTheConfiguredThickness()
    {
        using var surface = new VisualSurface { Size = new System.Drawing.Size(400, 300) };
        using var ruler = new VisualWorkflowRulerOverlay
        {
            Pan = new PointF(11200f, 11200f),
        };
        surface.Root = ruler;

        using var bitmap = new Bitmap(400, 300);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new System.Drawing.Size(400, 300));

        Assert.AreEqual(28f, ruler.LeftBandWidth, 0.01f, "long labels rotate instead of widening the band");
    }

    [TestMethod]
    public void RulerOverlayFollowsTheCanvasPan()
    {
        var model = CreateModel();
        using var surface = CreateSurface(model.Tree);

        Assert.IsTrue(surface.RulerOverlay.IsVisible);
        Assert.AreEqual(Width, surface.RulerOverlay.Bounds.Width, 0.5f);
        Assert.AreEqual(Height, surface.RulerOverlay.Bounds.Height, 0.5f);

        surface.ScrollBy(0d, 1d);

        Assert.AreEqual(surface.Canvas.Pan, surface.RulerOverlay.Pan);
    }

    private sealed class NodeAndSlotSelector : VisualTemplateSelector
    {
        public override VisualElement? SelectTemplate(object? item) => item switch
        {
            ISlotComponent => new TestSlotView(),
            INodeComponent => new Grid(),
            _ => null,
        };
    }

    private sealed class TestSlotView : Border;

    private sealed class ProbeSelector : VisualTemplateSelector
    {
        public override VisualElement? SelectTemplate(object? item)
            => item is INodeComponent ? new ProbeNodeView() : null;
    }

    private sealed class ProbeNodeView : Grid
    {
        public ProbeNodeView()
        {
            var design = new Grid { Width = 260f, Height = 180f };
            Probe.Width = 260f;
            Probe.Height = 180f;
            design.Add(Probe);
            Add(new Viewbox { Child = design, Stretch = VeloxDev.WinForms.Xaml.Controls.Stretch.Uniform });
        }

        public TransformProbe Probe { get; } = new();
    }

    private sealed class TransformProbe : VisualElement
    {
        public float RenderedScale { get; private set; } = 1f;

        protected override void OnRender(Graphics graphics)
            => RenderedScale = graphics.Transform.Elements[0];
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
        tree.SetHelper(new TreeHelper(200d));

        var helper = tree.GetHelper();
        var sender = new NodeDefaultComponent
        {
            Anchor = new Wf.Anchor(40d, 40d, 0),
            Size = new Wf.Size(260d, 180d),
        };
        var senderSlot = new SlotDefaultComponent { Channel = SlotChannel.OneTarget };
        senderSlot.Parent = sender;
        sender.Slots.Add(senderSlot);
        helper.CreateNode(sender);

        var receiver = new NodeDefaultComponent
        {
            Anchor = new Wf.Anchor(420d, 160d, 0),
            Size = new Wf.Size(260d, 180d),
        };
        var receiverSlot = new SlotDefaultComponent { Channel = SlotChannel.OneSource };
        receiverSlot.Parent = receiver;
        receiver.Slots.Add(receiverSlot);
        helper.CreateNode(receiver);

        helper.SendConnection(senderSlot);
        helper.ReceiveConnection(receiverSlot);

        return new Model(tree, sender, receiver);
    }

    private sealed record Model(ITreeComponent Tree, INodeComponent SenderNode, INodeComponent ReceiverNode);
}
