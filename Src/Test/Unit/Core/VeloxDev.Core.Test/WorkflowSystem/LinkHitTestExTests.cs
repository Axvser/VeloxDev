using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Tests for the shared link hit test — the algorithm an adapter calls to decide which link the pointer is on
/// (the answer then travels into <see cref="WorkflowInput"/> as the event's target).
/// </summary>
[TestClass]
public class LinkHitTestExTests : WorkflowInputTestBase
{
    [TestMethod]
    public void PublishCurve_MakesTheCurveReadableToTheHost()
    {
        // 曲线是公开事实：宿主想沿它画自己的东西（高亮、角标）时读的就是这一条，
        // 不必再按端口推一遍几何 —— 推两遍就会有两份会分叉的几何。
        var link = ReadyLink(0, 0, 100, 0);
        var curve = LinkCurve.BuildCubic(0, 0, 100, 0, 40);

        link.PublishCurve(curve, "visual");
        Assert.AreSame(curve, link.HitTarget()!.Curve);

        link.PublishCurve(null);
        Assert.IsNull(link.HitTarget()!.Curve);
    }

    [TestMethod]
    public void HitTestVisibleLinks_ReturnsTheTopmostLink()
    {
        // VisibleItems 的后段是后画的 ⇒ 压在上面，命中也该是它。
        var below = ReadyLink(0, 0, 100, 0);
        var above = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(below, above);

        Assert.AreSame(above, tree.HitTestVisibleLinks(50, 0, Radius));
    }

    [TestMethod]
    public void HitTestVisibleLinks_IgnoresTheTreesDragPreview()
    {
        // 橡皮筋就在指针底下 —— 它不存在于「谁被指到」这个问题里。
        var preview = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(preview);
        tree.VirtualLink = preview;

        Assert.IsNull(tree.HitTestVisibleLinks(50, 0, Radius));
    }

    [TestMethod]
    public void HitTestVisibleLinks_IgnoresAnInvisibleLink()
    {
        var hidden = ReadyLink(0, 0, 100, 0, visible: false);

        Assert.IsNull(TreeWith(hidden).HitTestVisibleLinks(50, 0, Radius));
    }

    [TestMethod]
    public void HitTestVisibleLinks_IgnoresALinkThatNeverPublishedACurve()
    {
        // 端点还没测量 ⇒ 视图一次都没画过 ⇒ 没有曲线 ⇒ 那里没有东西可点中。
        var a = new NodeDefaultViewModel();
        var b = new NodeDefaultViewModel();
        var sa = new SlotDefaultViewModel { Parent = a };
        var sb = new SlotDefaultViewModel { Parent = b };
        a.Slots.Add(sa);
        b.Slots.Add(sb);
        var link = new LinkDefaultViewModel { Sender = sa, Receiver = sb, IsVisible = true };

        Assert.IsNull(TreeWith(link).HitTestVisibleLinks(50, 0, Radius));
    }
}
