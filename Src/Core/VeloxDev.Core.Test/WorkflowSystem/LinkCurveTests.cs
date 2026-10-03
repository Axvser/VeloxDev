using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Unit tests for the link hit-test contract: the flattened curve (<see cref="LinkCurve"/>),
/// the optional capability (<see cref="ILinkHitTestable"/>) and the surface-side entry point
/// (<see cref="LinkHitTestEx"/>).
///
/// Contract: a link's view publishes the polyline it drew; a surface asks whether a point is within a
/// radius of it. Core assumes no particular shape — a cubic is only the house convenience — and a link
/// that is not render-ready is never hit, because nothing was drawn there.
/// </summary>
[TestClass]
public class LinkCurveTests
{
    private const double Radius = 6;

    // ── 形状 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildCubic_Endpoints_CurveStartsAndEndsOnThem()
    {
        var curve = LinkCurve.BuildCubic(10, 20, 310, 120, 40);

        Assert.AreEqual(10d, curve.XAt(0), 1e-9);
        Assert.AreEqual(20d, curve.YAt(0), 1e-9);
        Assert.AreEqual(310d, curve.XAt(curve.Count - 1), 1e-9);
        Assert.AreEqual(120d, curve.YAt(curve.Count - 1), 1e-9);
    }

    [TestMethod]
    public void BuildCubic_BothEnds_LeaveTheirPortHorizontally()
    {
        var curve = LinkCurve.BuildCubic(0, 0, 300, 200, 40);
        var last = curve.Count - 1;

        // 两个控制点的纵坐标各自跟着自己那一端 ⇒ 起止两端的切线是水平的。
        // 128 采样下第一段跨越 1/127 的参数，纵向只该挪动微不足道的一点。
        Assert.IsTrue(Math.Abs(curve.YAt(1) - curve.YAt(0)) < 1d);
        Assert.IsTrue(Math.Abs(curve.YAt(last) - curve.YAt(last - 1)) < 1d);
    }

    [TestMethod]
    public void BuildCubic_MinimumPull_KeepsClosePortsBowingOut()
    {
        // 两个端口几乎重叠时，0.5·dx 会让曲线退化成一条直线段。最小拉出量正是为它存在的：
        // 有它，曲线弓出到端点之外；没有它，曲线整条都落在两端之间。
        var without = LinkCurve.BuildCubic(0, 0, 10, 100, 0);
        var with = LinkCurve.BuildCubic(0, 0, 10, 100, 40);

        Assert.AreEqual(10d, without.Bounds.Right, 1e-9);
        Assert.IsTrue(with.Bounds.Right > 10);
    }

    // ── 命中 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Contains_PointOnTheLine_IsHit()
    {
        var curve = LinkCurve.FromPoints([0d, 100d], [0d, 0d]);

        Assert.IsTrue(curve.Contains(50, 0, Radius));
    }

    [TestMethod]
    public void Contains_FarFromTheLine_IsNotHit()
    {
        var curve = LinkCurve.FromPoints([0d, 100d], [0d, 0d]);

        Assert.IsFalse(curve.Contains(50, 500, Radius));
        Assert.IsFalse(curve.Contains(-500, 0, Radius));
    }

    [TestMethod]
    public void Contains_Radius_IsTheReachOnEitherSide()
    {
        var curve = LinkCurve.FromPoints([0d, 100d], [0d, 0d]);

        // 线外 5px 落在 6 半径内、落在 4 半径外 —— 半径就是判定面本身，不是包围盒。
        Assert.IsTrue(curve.Contains(50, 5, Radius));
        Assert.IsFalse(curve.Contains(50, 5, 4));
    }

    [TestMethod]
    public void Contains_PastTheEnds_IsNotHit()
    {
        // 线段之外的那一段延伸不算命中：判据是线段，不是两端连成的无限长直线。
        var curve = LinkCurve.FromPoints([0d, 100d], [0d, 0d]);

        Assert.IsFalse(curve.Contains(120, 0, Radius));
    }

    [TestMethod]
    public void Contains_EmptyCurve_IsNeverHit()
    {
        var curve = LinkCurve.FromPoints([], []);

        Assert.AreEqual(0, curve.Count);
        Assert.IsFalse(curve.Contains(0, 0, Radius));
    }

    [TestMethod]
    public void Contains_SinglePoint_HasNoStrokeToHit()
    {
        // 一个点构不成线段，没有「画出来的部分」可命中。构造器不会产出这种曲线，这是调用方的输入错误。
        var curve = LinkCurve.FromPoints([50d], [50d]);

        Assert.IsFalse(curve.Contains(50, 50, Radius));
    }

    [TestMethod]
    public void Contains_CoincidentEndpoints_HitsWithinTheRadius()
    {
        // 真正会出现的退化情形：连线手势的第一帧，两端落在同一点。
        // 那一段是零长段，判距要退回点到点，否则这个瞬间整条线都点不中。
        var curve = LinkCurve.FromPoints([50d, 50d], [50d, 50d]);

        Assert.IsTrue(curve.Contains(53, 54, Radius));
        Assert.IsFalse(curve.Contains(70, 70, Radius));
    }

    // ── 任意形状：Core 不假定贝塞尔 ──────────────────────────────────────────

    [TestMethod]
    public void FromPoints_ArbitraryElbow_HitsTheStrokeAndMissesTheShortcut()
    {
        // 一个直角折线 —— 不是贝塞尔。命中必须沿画出来的折线走：
        // 竖边上命中，而「拐角连成的斜边」那条捷径不算。
        var curve = LinkCurve.FromPoints([0d, 100d, 100d], [0d, 0d, 100d]);

        Assert.IsTrue(curve.Contains(100, 50, Radius));
        Assert.IsTrue(curve.Contains(50, 0, Radius));
        Assert.IsFalse(curve.Contains(85, 50, Radius));
        Assert.IsFalse(curve.Contains(50, 50, Radius));
    }

    [TestMethod]
    public void FromPoints_MismatchedLengths_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => LinkCurve.FromPoints([0d, 1d], [0d]));
    }

    // ── 弧长 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FromPoints_StraightLine_LengthIsTheDistance()
    {
        var curve = LinkCurve.FromPoints([0d, 3d], [0d, 4d]);

        Assert.AreEqual(5d, curve.Length, 1e-9);
    }

    [TestMethod]
    public void PointAtLength_MidpointOfAStraightLine_IsItsMiddle()
    {
        var curve = LinkCurve.FromPoints([0d, 3d], [0d, 4d]);

        var (x, y) = curve.PointAtLength(2.5);

        Assert.AreEqual(1.5, x, 1e-9);
        Assert.AreEqual(2d, y, 1e-9);
    }

    [TestMethod]
    public void PointAtLength_BeyondEitherEnd_ClampsToTheEnds()
    {
        var curve = LinkCurve.FromPoints([0d, 3d], [0d, 4d]);

        var (sx, sy) = curve.PointAtLength(-100);
        var (ex, ey) = curve.PointAtLength(100);

        Assert.AreEqual(0d, sx, 1e-9);
        Assert.AreEqual(0d, sy, 1e-9);
        Assert.AreEqual(3d, ex, 1e-9);
        Assert.AreEqual(4d, ey, 1e-9);
    }

    // ── 能力契约：默认 helper 就具备，且过渲染就绪门 ─────────────────────────

    private static IWorkflowNodeViewModel NodeWithSlots(params IWorkflowSlotViewModel[] slots)
    {
        var node = new NodeDefaultViewModel();
        foreach (var slot in slots)
        {
            slot.Parent = node;
            node.Slots.Add(slot);
        }
        return node;
    }

    private static IWorkflowLinkViewModel ReadyLink(out IWorkflowSlotViewModel sender, out IWorkflowSlotViewModel receiver)
    {
        var a = NodeWithSlots(new SlotDefaultViewModel());
        var b = NodeWithSlots(new SlotDefaultViewModel());
        sender = a.Slots[0];
        receiver = b.Slots[0];
        sender.Anchor = new Anchor(100, 50, 0);
        receiver.Anchor = new Anchor(300, 50, 0);
        return new LinkDefaultViewModel { Sender = sender, Receiver = receiver, IsVisible = true };
    }

    [TestMethod]
    public void HitTest_DefaultHelper_ImplementsTheCapability()
    {
        // 「模板默认可用」的落点：生成出来的项目用默认 helper，零代码就该具备这个能力。
        var link = ReadyLink(out _, out _);

        Assert.IsInstanceOfType(link.HitTarget(), typeof(ILinkHitTestable));
    }

    [TestMethod]
    public void HitTest_ThePublishedCurveAloneDecides_MeasurementIsNotConsulted()
    {
        // 门只看「有没有画出来的曲线」，不问锚点测没测到 —— 有的平台（Jalium）按设计从不写 slot.Anchor，
        // 用「锚点已测量」当门会让它整家连线静默失效，而且那回答的是模型而不是画出来的东西。
        // 视图画不出来时会把曲线撤回，所以「没有曲线」就是「那里没有东西」。
        var a = NodeWithSlots(new SlotDefaultViewModel());
        var b = NodeWithSlots(new SlotDefaultViewModel());
        var link = new LinkDefaultViewModel { Sender = a.Slots[0], Receiver = b.Slots[0], IsVisible = true };
        Assert.IsTrue(double.IsNaN(a.Slots[0].Anchor.Horizontal));

        link.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]));
        Assert.IsTrue(link.HitTest(50, 0, Radius));

        link.PublishCurve(null);
        Assert.IsFalse(link.HitTest(50, 0, Radius));
    }

    [TestMethod]
    public void HitTest_HiddenLink_IsNotHit()
    {
        var link = ReadyLink(out _, out _);
        link.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]));
        link.IsVisible = false;

        Assert.IsFalse(link.HitTest(50, 0, Radius));
    }

    [TestMethod]
    public void HitTest_MeasuredLinkWithPublishedCurve_ReportsTheHit()
    {
        var link = ReadyLink(out var sender, out var receiver);
        var curve = LinkCurve.BuildCubic(
            sender.Anchor.Horizontal, sender.Anchor.Vertical,
            receiver.Anchor.Horizontal, receiver.Anchor.Vertical,
            40);
        link.PublishCurve(curve);

        var (x, y) = curve.PointAtLength(curve.Length / 2);
        Assert.IsTrue(link.HitTest(x, y, Radius));
        Assert.IsFalse(link.HitTest(x + curve.Length, y, Radius));
    }

    [TestMethod]
    public void PublishCurve_Null_ClearsThePublishedShape()
    {
        var link = ReadyLink(out _, out _);
        link.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]));
        Assert.IsTrue(link.HitTest(50, 0, Radius));

        link.PublishCurve(null);

        Assert.IsFalse(link.HitTest(50, 0, Radius));
    }
}
