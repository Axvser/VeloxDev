using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The WPF-shaped attached behaviors on the visual surface: slot measurement, title-row node dragging and slot
/// connection, plus the tree-resource selector the surface resolves by default.
/// </summary>
[STATestClass]
public sealed class WorkflowSurfaceBehaviorTests
{
    [TestMethod]
    public void SlotLayoutBehavior_WritesSlotAnchorsFromNamedParts()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        var node = WorkflowVisualFixture.AddNode(
            fixture.Tree, 100d, 80d, SlotChannel.OneSource, SlotChannel.OneTarget, SlotChannel.OneTarget);
        fixture.Layout();

        AssertAnchor(node.Slots[0], 109d, 139d, "the named InPort part is measured");
        AssertAnchor(node.Slots[1], 270d, 129d, "the first slot-enumerator item is measured");
        AssertAnchor(node.Slots[2], 270d, 147d, "the second slot-enumerator item is measured");
    }

    [TestMethod]
    public void SlotLayoutBehavior_DisabledLeavesAnchorsUnset()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector { EnableSlotLayout = false });
        var node = WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d, SlotChannel.OneSource);
        fixture.Layout();

        Assert.IsTrue(double.IsNaN(node.Slots[0].Anchor.Horizontal), "an unmarked card does not publish slot anchors");
        Assert.IsTrue(double.IsNaN(node.Slots[0].Anchor.Vertical));
    }

    [TestMethod]
    public void NodeDragBehavior_DragsOnlyTheMarkedTitleRow()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        var node = WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        WorkflowSurfaceBehavior.ZoomAt(fixture.Host, PointF.Empty, 0.5d);
        fixture.Layout();

        var title = fixture.ToSurface(node.Anchor.Horizontal + 100d, node.Anchor.Vertical + 18d);
        var target = new PointF(title.X + 40f, title.Y + 20f);
        fixture.Host.ProcessPointerDown(title, MouseButtons.Left);
        fixture.Host.ProcessPointerMove(target);
        fixture.Host.ProcessPointerUp(target, MouseButtons.Left);

        Assert.AreEqual(180d, node.Anchor.Horizontal, 0.5d,
            "40 surface pixels at 50% are an 80 world-unit drag");
        Assert.AreEqual(120d, node.Anchor.Vertical, 0.5d);

        var body = fixture.ToSurface(node.Anchor.Horizontal + 100d, node.Anchor.Vertical + 78d);
        var bodyTarget = new PointF(body.X + 40f, body.Y + 20f);
        fixture.Host.ProcessPointerDown(body, MouseButtons.Left);
        fixture.Host.ProcessPointerMove(bodyTarget);
        fixture.Host.ProcessPointerUp(bodyTarget, MouseButtons.Left);

        Assert.AreEqual(180d, node.Anchor.Horizontal, 0.5d,
            "an unmarked body press does not drag the node");
        Assert.AreEqual(120d, node.Anchor.Vertical, 0.5d);
    }

    [TestMethod]
    public void NodeDragBehavior_UnmarkedTitleDoesNotDrag()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector { EnableDrag = false });
        var node = WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        var title = fixture.ToSurface(node.Anchor.Horizontal + 100d, node.Anchor.Vertical + 18d);
        var target = new PointF(title.X + 40f, title.Y + 20f);
        fixture.Host.ProcessPointerDown(title, MouseButtons.Left);
        fixture.Host.ProcessPointerMove(target);
        fixture.Host.ProcessPointerUp(target, MouseButtons.Left);

        Assert.AreEqual(100d, node.Anchor.Horizontal, 0.5d, "a title without the attached flag does not drag");
        Assert.AreEqual(80d, node.Anchor.Vertical, 0.5d);
    }

    [TestMethod]
    public void NodeDragBehavior_MeasuresTheDeltaInTheNamedCoordinateHost()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector
        {
            DragSpaceScale = 2d,
            DragCoordinateHost = "PART_Local",
        });
        var node = WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        fixture.Layout();

        var title = FindByName(fixture.NodeView(node), "Title");
        Assert.IsNotNull(title, "the title row is part of the card template");
        var center = title.AbsoluteBounds;
        var press = new PointF(center.X + (center.Width / 2f), center.Y + (center.Height / 2f));
        var target = new PointF(press.X + 40f, press.Y + 20f);

        fixture.Host.ProcessPointerDown(press, MouseButtons.Left);
        fixture.Host.ProcessPointerMove(target);
        fixture.Host.ProcessPointerUp(target, MouseButtons.Left);

        Assert.AreEqual(120d, node.Anchor.Horizontal, 0.5d,
            "the 2x coordinate host turns a 40-pixel surface drag into 20 world units");
        Assert.AreEqual(90d, node.Anchor.Vertical, 0.5d);
    }

    [TestMethod]
    public void SlotConnectionBehavior_EnabledSlotStartsTheConnection()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        var node = WorkflowVisualFixture.AddNode(
            fixture.Tree, 100d, 80d, SlotChannel.OneSource, SlotChannel.OneTarget);
        fixture.Layout();

        var point = fixture.ToSurface(node.Slots[1].Anchor.Horizontal, node.Slots[1].Anchor.Vertical);
        fixture.Host.ProcessPointerDown(point, MouseButtons.Left);

        Assert.IsTrue(fixture.Tree.VirtualLink.IsVisible, "pressing a sender starts the preview link");
        Assert.AreSame(node.Slots[1], WorkflowInput.For(fixture.Tree).ConnectionSender,
            "the pressed slot is the connection sender");
        Assert.IsTrue(node.Slots[1].State.HasFlag(SlotState.PreviewSender));
        Assert.IsEmpty(fixture.Tree.Links);
    }

    [TestMethod]
    public void SlotConnectionBehavior_ExplicitlyDisabledSlotDoesNotStart()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector { EnableConnection = false });
        var node = WorkflowVisualFixture.AddNode(
            fixture.Tree, 100d, 80d, SlotChannel.OneSource, SlotChannel.OneTarget);
        fixture.Layout();

        var point = fixture.ToSurface(node.Slots[1].Anchor.Horizontal, node.Slots[1].Anchor.Vertical);
        fixture.Host.ProcessPointerDown(point, MouseButtons.Left);

        Assert.IsFalse(fixture.Tree.VirtualLink.IsVisible, "an explicitly disabled slot does not start a preview");
        Assert.IsNull(WorkflowInput.For(fixture.Tree).ConnectionSender);
        Assert.IsEmpty(fixture.Tree.Links);
    }

    [TestMethod]
    public void SlotConnectionBehavior_ReceiverCompletesTheConnection()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        var senderNode = WorkflowVisualFixture.AddNode(
            fixture.Tree, 100d, 80d, SlotChannel.OneSource, SlotChannel.OneTarget);
        var receiverNode = WorkflowVisualFixture.AddNode(fixture.Tree, 420d, 80d, SlotChannel.OneSource);
        fixture.Layout();

        var sender = senderNode.Slots[1];
        var receiver = receiverNode.Slots[0];

        // VisualHost 把松开事件送给按下时的捕获元素，两点之间的完整手势到不了接收端的视图；
        // 这里用 Core 的发送命令建立拖拽状态，再让接收端视图真正收到一次 pointer up。
        sender.SendConnectionCommand.Execute(null);
        Assert.IsTrue(fixture.Tree.VirtualLink.IsVisible, "the connection drag is live");

        var receiverPoint = fixture.ToSurface(receiver.Anchor.Horizontal, receiver.Anchor.Vertical);
        fixture.Host.ProcessPointerMove(receiverPoint);
        Assert.AreSame(receiver, WorkflowInput.For(fixture.Tree).PreviewReceiver,
            "moving over the receiver publishes it as the preview target");

        fixture.Host.ProcessPointerUp(receiverPoint, MouseButtons.Left);

        Assert.HasCount(1, fixture.Tree.Links, "releasing on the receiver completes the connection");
        Assert.AreSame(sender, fixture.Tree.Links[0].Sender);
        Assert.AreSame(receiver, fixture.Tree.Links[0].Receiver);
        Assert.IsFalse(fixture.Tree.VirtualLink.IsVisible, "the preview link is retracted after the drop");
    }

    [TestMethod]
    public void Surface_OrdersLinkViewsBelowNodeViews()
    {
        using var fixture = WorkflowVisualFixture.Create(new NodeCardSelector());
        WorkflowVisualFixture.AddNode(fixture.Tree, 100d, 80d);
        WorkflowVisualFixture.AddNode(fixture.Tree, 420d, 80d);
        fixture.Layout();

        var linkIndex = -1;
        var nodeIndex = -1;
        for (var index = 0; index < fixture.Canvas.Children.Count; index++)
        {
            var view = (VisualElement)fixture.Canvas.Children[index];
            if (linkIndex < 0 && view.DataContext is ILinkComponent)
            {
                linkIndex = index;
                Assert.AreEqual(-1, VC.Panel.GetZIndex(view), "link views sit on the -1 layer");
            }

            if (nodeIndex < 0 && view.DataContext is INodeComponent)
            {
                nodeIndex = index;
                Assert.AreEqual(0, VC.Panel.GetZIndex(view), "node cards sit on the 0 layer");
            }
        }

        Assert.IsGreaterThanOrEqualTo(0, linkIndex, "the preview link view exists in the pool");
        Assert.IsGreaterThan(linkIndex, nodeIndex, "every link view is ordered below every node view");
    }

    [TestMethod]
    public void Surface_ResolvesTheSelectorFromTheRootResources()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        var node = WorkflowVisualFixture.AddNode(tree, 100d, 80d);

        var selector = new NodeCardSelector();
        var canvas = new VC.Canvas { Name = "PART_Canvas", IsHitTestVisible = true };
        canvas.Resources["WorkflowTemplateSelector"] = selector;

        using var host = new VisualHost { Size = new DrawingSize(WorkflowVisualFixture.Width, WorkflowVisualFixture.Height) };
        host.Root = canvas;
        WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
        WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
        WorkflowSurfaceBehavior.SetIsEnabled(host, true);

        using (var bitmap = new Bitmap(1, 1))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            host.RenderTo(graphics, new DrawingSize(WorkflowVisualFixture.Width, WorkflowVisualFixture.Height));
            host.RenderTo(graphics, new DrawingSize(WorkflowVisualFixture.Width, WorkflowVisualFixture.Height));
        }

        var card = canvas.Children.OfType<VisualElement>().FirstOrDefault(static child => child.DataContext is INodeComponent);
        Assert.IsNotNull(card, "the default resource key resolved a selector and the pool materialized the node");
        Assert.AreSame(node, card.DataContext);

        WorkflowSurfaceBehavior.SetWorkflowTree(host, null);
        WorkflowSurfaceBehavior.SetIsEnabled(host, false);
    }

    [TestMethod]
    public void Surface_ScrollBarsFollowTheWorldBounds()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        tree.Layout.OriginSize = new VeloxDev.WorkflowSystem.Size(300d, 200d);
        var node = WorkflowVisualFixture.AddNode(tree, 500d, 50d);

        var canvas = new VC.Canvas { Name = "PART_Canvas", IsHitTestVisible = true };
        var scroll = new VC.ScrollViewer { Name = "PART_ScrollViewer", Content = canvas };

        using var host = new VisualHost { Size = new DrawingSize(400, 300) };
        host.Root = scroll;
        WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
        WorkflowSurfaceBehavior.SetScrollViewerName(host, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
        WorkflowSurfaceBehavior.SetIsEnabled(host, true);

        Render(host, canvas);

        Assert.IsTrue(scroll.IsExternalScroll, "the surface drives the viewer's bars");
        Assert.AreEqual(700d, scroll.ExtentWidth, 0.5d, "the node beyond the origin size widens the range");

        node.Anchor = new Anchor(900d, node.Anchor.Vertical, node.Anchor.Layer);
        Render(host, canvas);
        Assert.AreEqual(1100d, scroll.ExtentWidth, 0.5d, "dragging the node further extends the range");

        WorkflowSurfaceBehavior.ZoomAt(host, PointF.Empty, 2d);
        Render(host, canvas);
        Assert.AreEqual(2200d, scroll.ExtentWidth, 0.5d, "zoom scales the scrollable range");

        WorkflowSurfaceBehavior.SetWorkflowTree(host, null);
        WorkflowSurfaceBehavior.SetIsEnabled(host, false);
    }

    [TestMethod]
    public void Surface_ScrollBarsGrowWhenTheCameraPansPastTheContent()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        tree.Layout.OriginSize = new VeloxDev.WorkflowSystem.Size(300d, 200d);
        WorkflowVisualFixture.AddNode(tree, 500d, 50d);

        var canvas = new VC.Canvas { Name = "PART_Canvas", IsHitTestVisible = true };
        var scroll = new VC.ScrollViewer { Name = "PART_ScrollViewer", Content = canvas };

        using var host = new VisualHost { Size = new DrawingSize(400, 300) };
        host.Root = scroll;
        WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
        WorkflowSurfaceBehavior.SetScrollViewerName(host, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
        WorkflowSurfaceBehavior.SetIsEnabled(host, true);

        Render(host, canvas);
        var before = scroll.ExtentWidth;
        Assert.AreEqual(700d, before, 0.5d);

        // 相机向右拖出内容左边缘：可见内容区进入世界负坐标，画布/ScrollViewer 的 extent 应随之长大。
        WorkflowSurfaceBehavior.SetPan(host, new PointF(1000f, 0f));
        Render(host, canvas);

        Assert.IsGreaterThan(before, scroll.ExtentWidth,
            "panning past the content edge extends the scrollable range");
        var expanded = scroll.ExtentWidth;

        // 拖回内容区后范围保持扩展，不缩回原长度。
        WorkflowSurfaceBehavior.SetPan(host, new PointF(0f, 0f));
        Render(host, canvas);

        Assert.AreEqual(expanded, scroll.ExtentWidth, 0.5d,
            "returning inside the content keeps the expanded range");

        WorkflowSurfaceBehavior.SetWorkflowTree(host, null);
        WorkflowSurfaceBehavior.SetIsEnabled(host, false);
    }

    private static void Render(VisualHost host, VisualElement canvas)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        var size = host.Size;
        host.RenderTo(graphics, size);
        canvas.InvalidateMeasure();
        host.RenderTo(graphics, size);
        // ApplyCamera resizes the canvas at the end of the previous pass, so the viewer sees the new content size
        // one pass later.
        host.RenderTo(graphics, size);
    }

    private static void AssertAnchor(ISlotComponent slot, double expectedX, double expectedY, string message)
    {
        Assert.AreEqual(expectedX, slot.Anchor.Horizontal, 1d, message);
        Assert.AreEqual(expectedY, slot.Anchor.Vertical, 1d, message);
    }

    private static VisualElement? FindByName(VisualElement root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindByName(element, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
