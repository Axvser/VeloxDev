using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// A link that a node card is drawn over is <b>not</b> the thing the pointer is on: the card is opaque, so that
/// part of the line is not drawn, and the hit test must not answer for it. The link's own ports are the one
/// exception — they are drawn on the card's edge, so stopping on one still means this link.
/// </summary>
[TestClass]
public sealed class LinkOcclusionTests
{
    private const double Radius = LinkHitTestEx.DefaultHitRadius;

    // 卡片盖住连线中段：线上的点被卡片吃掉，卡片外的同一段线照常命中。
    [TestMethod]
    public void ANodeDrawnOverTheLink_TakesTheHitAway()
    {
        var (tree, link) = Scene();

        Assert.IsNull(tree.HitTestVisibleLinks(50, 0, Radius), "the point sits under the card");
        Assert.AreSame(link, tree.HitTestVisibleLinks(80, 0, Radius), "the same line, past the card's edge");
    }

    [TestMethod]
    public void MovingTheCardAway_GivesTheHitBack()
    {
        var (tree, link) = Scene();
        var card = (IWorkflowNodeViewModel)tree.GetHelper().VisibleItems[0];

        card.Anchor = new Anchor(0, -400, 0);

        Assert.AreSame(link, tree.HitTestVisibleLinks(50, 0, Radius));
    }

    // 端口不算遮挡：指针停在端口上（离这条线自己的端点 radius 以内）仍然算命中，
    // 哪怕端口画在卡片边缘、那一点就在卡片框内。
    [TestMethod]
    public void StoppingOnItsOwnPort_StillHitsTheLink()
    {
        var (tree, link) = Scene();

        Assert.AreSame(link, tree.HitTestVisibleLinks(0, 0, Radius), "source port, on the card's edge");
        Assert.AreSame(link, tree.HitTestVisibleLinks(100, 0, Radius), "receiver port, on the other card");
        Assert.AreSame(link, tree.HitTestVisibleLinks(0, 3, Radius), "just beside the source port, still within reach");
    }

    // 豁免只到端口周围那一圈：再往里走，卡片照样遮挡。
    [TestMethod]
    public void ThePortExemptionIsLocal_NotTheWholeCard()
    {
        var (tree, _) = Scene();

        Assert.IsNull(tree.HitTestVisibleLinks(0, Radius + 4, Radius), "past the port allowance, the card covers");
    }

    // 场景：一条从 (0,0) 到 (100,0) 的线，两端各一张卡；左边那张卡**盖住线的前半段**。
    private static (IWorkflowTreeViewModel Tree, IWorkflowLinkViewModel Link) Scene()
    {
        var left = new NodeDefaultViewModel { Anchor = new Anchor(0, 0, 0), Size = new Size { Width = 60, Height = 40 } };
        var right = new NodeDefaultViewModel { Anchor = new Anchor(100, 0, 0), Size = new Size { Width = 60, Height = 40 } };
        var sender = new SlotDefaultViewModel { Parent = left, Anchor = new Anchor(0, 0, 0) };
        var receiver = new SlotDefaultViewModel { Parent = right, Anchor = new Anchor(100, 0, 0) };
        left.Slots.Add(sender);
        right.Slots.Add(receiver);

        var link = new LinkDefaultViewModel { Sender = sender, Receiver = receiver, IsVisible = true };
        link.PublishCurve(LinkCurve.BuildCubic(0, 0, 100, 0, 40));

        var tree = new TreeDefaultViewModel();
        var visible = tree.GetHelper().VisibleItems;
        visible.Clear();
        visible.Add(left);
        visible.Add(right);
        visible.Add(link);
        return (tree, link);
    }
}
