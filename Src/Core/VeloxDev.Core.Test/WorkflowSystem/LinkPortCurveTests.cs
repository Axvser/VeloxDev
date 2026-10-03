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
        // 拖拽预览的自由端下面不是端口，**不能替它假设一条边** —— 零向量表示「没有方向」。
        AssertDirection((0, 0), new SlotDefaultViewModel());
    }

    [TestMethod]
    public void PortCurvePoints_FreeEnd_ArrivesStraightAndAssumesNothing()
    {
        // 预览：起点是真实端口（有自己的法线），末端是指针（没有节点）。末端那一半不能拉弯 ——
        // 它的控制点就落在端点上，曲线是「从一个端口出去、到指针处是直的」。而且曲线**不能**因为
        // 假设末端在某个方向就越界绕一下。
        var source = SlotOn(Node(0, 0, 200, 100), 200, 50);
        var freeEnd = new SlotDefaultViewModel { Anchor = new Anchor(320, 240, 0) };

        var points = LinkCurve.PortCurvePoints(source, freeEnd, Minimum);

        Assert.AreEqual(320d, points[2].X, 1e-9);
        Assert.AreEqual(240d, points[2].Y, 1e-9);
        Assert.IsTrue(points[1].X > 200d, "起点那一半仍按自己的法线拉");

        var curve = LinkCurve.BuildPortCubic(source, freeEnd, Minimum);
        for (var i = 0; i < curve.Count; i++)
        {
            Assert.IsTrue(curve.XAt(i) >= 200d - 1e-9, $"sample {i} must stay on the outward side of the source port");
        }
    }

    [TestMethod]
    public void PortCurvePoints_StandardLayout_IsExactlyTheHouseCurve()
    {
        // 发送口在右、接收口在左 ⇒ 两个法线都是水平的，拉法与旧公式逐字相同：七家的观感因此不变。
        var sender = SlotOn(Node(0, 0, 200, 100), 200, 50);
        var receiver = SlotOn(Node(300, 0, 200, 100), 300, 50);

        var points = LinkCurve.PortCurvePoints(sender, receiver, Minimum);
        var house = LinkCurve.BuildCubic(200, 50, 300, 50, Minimum);

        var portCurve = LinkCurve.BuildPortCubic(sender, receiver, Minimum);
        AssertSamePolyline(house, portCurve);

        Assert.AreEqual(200d + 50d, points[1].X, 1e-9);   // pull = |dx|/2 = 50
        Assert.AreEqual(50d, points[1].Y, 1e-9);
        Assert.AreEqual(300d - 50d, points[2].X, 1e-9);
        Assert.AreEqual(50d, points[2].Y, 1e-9);
    }

    [TestMethod]
    public void PortCurvePoints_SwappingTheEnds_GivesTheSameCurveReversed()
    {
        var a = SlotOn(Node(0, 0, 200, 100), 200, 40);
        var b = SlotOn(Node(-120, 260, 200, 100), -120, 300);

        var forward = LinkCurve.PortCurvePoints(a, b, Minimum);
        var backward = LinkCurve.PortCurvePoints(b, a, Minimum);

        for (var i = 0; i < forward.Length; i++)
        {
            var mirrored = backward[forward.Length - 1 - i];
            Assert.AreEqual(forward[i].X, mirrored.X, 1e-9, $"point {i} x");
            Assert.AreEqual(forward[i].Y, mirrored.Y, 1e-9, $"point {i} y");
        }

        // 采样出来的折线是同一条曲线的**反向遍历**：同一个几何，不是两条线。
        AssertSamePolylineReversed(LinkCurve.BuildPortCubic(a, b, Minimum), LinkCurve.BuildPortCubic(b, a, Minimum));
    }

    [TestMethod]
    public void PortCurvePoints_NeverFoldsIntoItsOwnNode()
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
            AssertPulledOutward(a, b);
            AssertPulledOutward(b, a);
        }
    }

    [TestMethod]
    public void PortCurvePoints_BackwardsLayout_PullsBothEndsOutOfTheirOwnNodes()
    {
        // 目标在左边时：两个控制点仍然各自贴着自己的口朝外拉（这条就是原则本身）。曲线本体在两口之间
        // 会从节点上方经过 —— 单个三次曲线不绕行，这是模型的边界，不是这条规则的产物（Blender/UE 同样如此）。
        var senderNode = Node(300, 0, 200, 100);
        var receiverNode = Node(0, 0, 200, 100);
        var sender = SlotOn(senderNode, 500, 50);
        var receiver = SlotOn(receiverNode, 0, 50);

        var points = LinkCurve.PortCurvePoints(sender, receiver, Minimum);

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

    private static bool Inside(IWorkflowNodeViewModel node, double x, double y)
        => x > node.Anchor.Horizontal + 1e-6
        && x < node.Anchor.Horizontal + node.Size.Width - 1e-6
        && y > node.Anchor.Vertical + 1e-6
        && y < node.Anchor.Vertical + node.Size.Height - 1e-6;

    private static void AssertPulledOutward(IWorkflowSlotViewModel port, IWorkflowSlotViewModel other)
    {
        var (nx, ny) = LinkCurve.PortOutward(port);
        var points = LinkCurve.PortCurvePoints(port, other, Minimum);
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
