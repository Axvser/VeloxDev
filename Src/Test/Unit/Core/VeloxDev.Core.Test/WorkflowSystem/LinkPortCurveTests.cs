using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The rule the port-aware curve encodes: <b>each control point is pulled along the outward normal of the edge
/// its own port sits on</b> — never into the node — and the two ends are treated symmetrically, so which end is
/// called the sender does not change the curve.
/// </summary>
[TestClass]
public sealed class LinkPortCurveTests
{
    private const double Minimum = 40d;

    [TestMethod]
    public void PortOutward_IsTheEdgeThePortSitsOn()
    {
        var node = Node(0, 0, 200, 100);

        AssertDirection((1, 0), SlotOn(node, 200, 50));   // 右边
        AssertDirection((-1, 0), SlotOn(node, 0, 50));    // 左边
        AssertDirection((0, -1), SlotOn(node, 100, 0));   // 上边
        AssertDirection((0, 1), SlotOn(node, 100, 100));  // 下边
    }

    [TestMethod]
    public void PortOutward_APortWithNoNode_HasNoDirection()
    {
        // 没有节点的口报零向量 = 「不知道方向」，而不是替它猜一条边。
        AssertDirection((0, 0), new SlotDefaultViewModel());
    }

    [TestMethod]
    public void LinkCurvePoints_AnEndWithNoNode_FallsBackToTheHouseRule()
    {
        // 拖拽预览的两端都是占位插槽（Parent 为 null），没有边可读。此时**整条**退回房规，
        // 否则两端都不拉，橡皮筋会被拉成一条直线。
        var source = SlotOn(Node(0, 0, 200, 100), 200, 50);
        var freeEnd = new SlotDefaultViewModel { Anchor = new Anchor(320, 240, 0) };

        var points = LinkCurvePoints(Link(source, freeEnd));
        var house = LinkCurve.BuildCubic(200, 50, 320, 240, Minimum);

        Assert.AreEqual(house.XAt(0), points[0].X, 1e-9);
        Assert.AreEqual(house.XAt(house.Count - 1), points[3].X, 1e-9);
        Assert.IsTrue(points[1].X > 200d, "房规下起点那一半照常朝外拉");
        Assert.AreNotEqual(freeEnd.Anchor.Horizontal, points[2].X, "末端也拉，所以曲线不是直的");
    }

    [TestMethod]
    public void PortOutward_TakesTheDirectionFromTheGivenPoint_NotFromTheSlotAnchor()
    {
        // 判据只有「这个点相对父节点在哪条边」。故意把 slot.Anchor 设成 NaN（模型自己推端口位置的适配器
        // 就是这样，如 Jalium）：方向照样由传进来的坐标决定。
        var node = Node(0, 0, 200, 100);
        var slot = SlotOn(node, 100, 0);                      // 上边
        slot.Anchor = new Anchor(double.NaN, double.NaN, 0);

        var up = LinkCurve.PortOutward(100, 0, node);
        Assert.AreEqual(0d, up.X, 1e-9);
        Assert.AreEqual(-1d, up.Y, 1e-9);

        var left = LinkCurve.PortOutward(0, 50, node);
        Assert.AreEqual(-1d, left.X, 1e-9);
        Assert.AreEqual(0d, left.Y, 1e-9);
    }

    [TestMethod]
    public void LinkCurvePoints_StandardLayout_IsExactlyTheHouseCurve()
    {
        // 发送口在右、接收口在左 ⇒ 两个法线都是水平的，拉法与旧公式逐字相同：七家的观感因此不变。
        var sender = SlotOn(Node(0, 0, 200, 100), 200, 50);
        var receiver = SlotOn(Node(300, 0, 200, 100), 300, 50);
        var link = Link(sender, receiver);

        var points = LinkCurvePoints(link);
        var house = LinkCurve.BuildCubic(200, 50, 300, 50, Minimum);

        AssertSamePolyline(house, LinkCurve.BuildLinkCubic(link, 200, 50, 300, 50, Minimum));

        Assert.AreEqual(200d + 50d, points[1].X, 1e-9);   // pull = |dx|/2 = 50
        Assert.AreEqual(50d, points[1].Y, 1e-9);
        Assert.AreEqual(300d - 50d, points[2].X, 1e-9);
        Assert.AreEqual(50d, points[2].Y, 1e-9);
    }

    [TestMethod]
    public void LinkCurvePoints_SwappingTheEnds_GivesTheSameCurveReversed()
    {
        var a = SlotOn(Node(0, 0, 200, 100), 200, 40);
        var b = SlotOn(Node(-120, 260, 200, 100), -120, 300);

        var forward = LinkCurvePoints(Link(a, b));
        var backward = LinkCurvePoints(Link(b, a));

        for (var i = 0; i < forward.Length; i++)
        {
            var mirrored = backward[forward.Length - 1 - i];
            Assert.AreEqual(forward[i].X, mirrored.X, 1e-9, $"point {i} x");
            Assert.AreEqual(forward[i].Y, mirrored.Y, 1e-9, $"point {i} y");
        }

        // 采样出来的折线是同一条曲线的**反向遍历**：同一个几何，不是两条线。
        AssertSamePolylineReversed(LinkCurve.BuildLinkCubic(Link(a, b), 200, 40, -120, 300, Minimum), LinkCurve.BuildLinkCubic(Link(b, a), -120, 300, 200, 40, Minimum));
    }

    [TestMethod]
    public void LinkCurvePoints_NeverFoldsIntoItsOwnNode()
    {
        // 四种摆位都试：正常左右、目标在左（反向）、上边口、下边口。每个控制点都必须在该口法线的**外侧**。
        var cases = new (IWorkflowSlotViewModel A, IWorkflowSlotViewModel B)[]
        {
            (SlotOn(Node(0, 0, 200, 100), 200, 50), SlotOn(Node(300, 0, 200, 100), 300, 50)),
            (SlotOn(Node(300, 0, 200, 100), 300, 50), SlotOn(Node(0, 0, 200, 100), 200, 50)),
            (SlotOn(Node(0, 0, 200, 100), 100, 0), SlotOn(Node(300, 0, 200, 100), 300, 50)),
            (SlotOn(Node(0, 0, 200, 100), 100, 100), SlotOn(Node(300, 0, 200, 100), 300, 50)),
        };

        foreach (var (a, b) in cases)
        {
            AssertPulledOutward(Link(a, b));
            AssertPulledOutward(Link(b, a));
        }
    }

    [TestMethod]
    public void LinkCurvePoints_BackwardsLayout_PullsBothEndsOutOfTheirOwnNodes()
    {
        // 目标在左边时：两个控制点仍然各自贴着自己的口朝外拉（这条就是原则本身）。曲线本体在两口之间
        // 会从节点上方经过 —— 单个三次曲线不绕行，这是模型的边界，不是这条规则的产物（Blender/UE 同样如此）。
        var senderNode = Node(300, 0, 200, 100);
        var receiverNode = Node(0, 0, 200, 100);
        var sender = SlotOn(senderNode, 500, 50);
        var receiver = SlotOn(receiverNode, 0, 50);

        var points = LinkCurvePoints(Link(sender, receiver));

        Assert.IsTrue(points[1].X > senderNode.Anchor.Horizontal + senderNode.Size.Width,
            "sender's control point must sit beyond its node's right edge");
        Assert.IsTrue(points[2].X < receiverNode.Anchor.Horizontal,
            "receiver's control point must sit beyond its node's left edge");
    }

    // ── 工具 ────────────────────────────────────────────────────────────────

    private static IWorkflowNodeViewModel Node(double x, double y, double width, double height)
    {
        var node = new NodeDefaultViewModel();
        node.Anchor = new Anchor(x, y, 0);
        node.Size = new Size { Width = width, Height = height };
        return node;
    }

    private static IWorkflowSlotViewModel SlotOn(IWorkflowNodeViewModel node, double x, double y)
    {
        var slot = new SlotDefaultViewModel { Anchor = new Anchor(x, y, 0) };
        slot.Parent = node;
        node.Slots.Add(slot);
        return slot;
    }

    // 端点坐标一律取两口自己的 Anchor —— 与视图从绑定里读到的那两个数是同一份
    // （视图的 StartLeft/Top 就绑在 Sender.Anchor 上），所以画的与发布的不会各推一遍。
    private static LinkDefaultViewModel Link(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        => new() { Sender = sender, Receiver = receiver };

    private static (double X, double Y)[] LinkCurvePoints(LinkDefaultViewModel link)
        => LinkCurve.LinkCurvePoints(
            link,
            link.Sender.Anchor.Horizontal, link.Sender.Anchor.Vertical,
            link.Receiver.Anchor.Horizontal, link.Receiver.Anchor.Vertical,
            Minimum);

    private static void AssertPulledOutward(LinkDefaultViewModel link)
    {
        var (nx, ny) = LinkCurve.PortOutward(link.Sender);
        var points = LinkCurve.LinkCurvePoints(
            link,
            link.Sender.Anchor.Horizontal, link.Sender.Anchor.Vertical,
            link.Receiver.Anchor.Horizontal, link.Receiver.Anchor.Vertical,
            Minimum);
        var control = points[1];

        // 端点 + 控制点：控制点在法线方向上的投影必须不小于端点自己的 —— 也就是绝不朝节点里折。
        var start = points[0];
        var along = ((control.X - start.X) * nx) + ((control.Y - start.Y) * ny);
        Assert.IsTrue(along > 0, $"control point folds inward (projection {along})");
    }

    private static void AssertDirection((double X, double Y) expected, IWorkflowSlotViewModel slot)
    {
        var actual = LinkCurve.PortOutward(slot);
        Assert.AreEqual(expected.X, actual.X, 1e-9);
        Assert.AreEqual(expected.Y, actual.Y, 1e-9);
    }

    private static void AssertSamePolylineReversed(LinkCurve forward, LinkCurve backward)
    {
        Assert.AreEqual(forward.Count, backward.Count);
        for (var i = 0; i < forward.Count; i++)
        {
            var mirror = backward.Count - 1 - i;
            Assert.AreEqual(forward.XAt(i), backward.XAt(mirror), 1e-9, $"sample {i} x");
            Assert.AreEqual(forward.YAt(i), backward.YAt(mirror), 1e-9, $"sample {i} y");
        }
    }

    private static void AssertSamePolyline(LinkCurve expected, LinkCurve actual)
    {
        Assert.AreEqual(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected.XAt(i), actual.XAt(i), 1e-9, $"sample {i} x");
            Assert.AreEqual(expected.YAt(i), actual.YAt(i), 1e-9, $"sample {i} y");
        }
    }
}
