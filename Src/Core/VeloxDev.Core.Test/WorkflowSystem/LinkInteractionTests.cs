using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Unit tests for the link interaction hub: adapters forward translated pointer/key events in, and the
/// hub decides — once, for every platform — which link is under the pointer and what hover/selection
/// that means.
/// </summary>
[TestClass]
public class LinkInteractionTests
{
    private const double Radius = LinkHitTestEx.DefaultHitRadius;

    private static IWorkflowLinkViewModel ReadyLink(double sx, double sy, double ex, double ey, bool visible = true)
    {
        var a = new NodeDefaultViewModel();
        var b = new NodeDefaultViewModel();
        var sa = new SlotDefaultViewModel { Parent = a };
        var sb = new SlotDefaultViewModel { Parent = b };
        a.Slots.Add(sa);
        b.Slots.Add(sb);
        sa.Anchor = new Anchor(sx, sy, 0);
        sb.Anchor = new Anchor(ex, ey, 0);

        var link = new LinkDefaultViewModel { Sender = sa, Receiver = sb, IsVisible = visible };
        link.PublishCurve(LinkCurve.BuildCubic(sx, sy, ex, ey, 40));
        return link;
    }

    private static TreeDefaultViewModel TreeWith(params IWorkflowLinkViewModel[] links)
    {
        var tree = new TreeDefaultViewModel();
        var visible = tree.GetHelper().VisibleItems;
        visible.Clear();
        foreach (var link in links) visible.Add(link);
        return tree;
    }

    private static PointerEvent Move(double x, double y)
        => new(PointerPhase.Moved, new Anchor(x, y, 0));

    // ── 悬停 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Publish_MoveOntoALink_HoversIt()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));

        interaction.Publish(Move(50, 0));

        Assert.AreSame(link, interaction.HoveredLink);
    }

    [TestMethod]
    public void Publish_MoveAwayFromEveryLink_ClearsTheHover()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));

        interaction.Publish(Move(50, 500));

        Assert.IsNull(interaction.HoveredLink);
    }

    [TestMethod]
    public void Publish_PointerLeavesTheSurface_ClearsTheHover()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));

        interaction.Publish(new PointerEvent(PointerPhase.Exited, new Anchor(50, 0, 0)));

        Assert.IsNull(interaction.HoveredLink);
    }

    [TestMethod]
    public void Publish_HoverChangesOncePerLink_NotOncePerMove()
    {
        // 悬停是一项状态，不是一条流 —— 同一条线上的连续移动不该反复报「选中变了」。
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        var changes = 0;
        interaction.HoverChanged += (_, _) => changes++;

        interaction.Publish(Move(40, 0));
        interaction.Publish(Move(50, 0));
        interaction.Publish(Move(60, 0));

        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void Publish_Suspended_MovementDoesNotChangeTheHover()
    {
        // 宿主的菜单开着时指针在菜单上，那段时间的移动不该把菜单针对的那条线取消选中。
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));
        interaction.IsSuspended = true;

        interaction.Publish(Move(50, 500));

        Assert.AreSame(link, interaction.HoveredLink);
    }

    // ── 按下 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Publish_RightPressOnALink_SelectsItThenRaisesPressed()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        IWorkflowLinkViewModel? pressed = null;
        var button = PointerButtonKind.None;
        interaction.LinkPressed += (_, e) => { pressed = e.Link; button = e.Button; };

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 0, 0), PointerButtonKind.Right));

        Assert.AreSame(link, interaction.HoveredLink);
        Assert.AreSame(link, pressed);
        Assert.AreEqual(PointerButtonKind.Right, button);
    }

    [TestMethod]
    public void Publish_PressAwayFromEveryLink_RaisesNothingAndClearsTheHover()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));
        var raised = false;
        interaction.LinkPressed += (_, _) => raised = true;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 500, 0), PointerButtonKind.Right));

        Assert.IsFalse(raised);
        Assert.IsNull(interaction.HoveredLink);
    }

    // ── 键盘 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Publish_DeleteKeyWithAHoveredLink_RequestsItsDeletion()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));
        IWorkflowLinkViewModel? requested = null;
        interaction.LinkDeleteRequested += (_, e) => requested = e.Link;

        interaction.Publish(new KeyEvent(InputKey.Delete));

        // 只是请求：删不删由宿主决定（确认、事务、撤销组都可能有话说）。
        Assert.AreSame(link, requested);
    }

    [TestMethod]
    public void Publish_DeleteKeyWithNothingHovered_RequestsNothing()
    {
        var interaction = new LinkInteraction(TreeWith(ReadyLink(0, 0, 100, 0)));
        var raised = false;
        interaction.LinkDeleteRequested += (_, _) => raised = true;

        interaction.Publish(new KeyEvent(InputKey.Delete));

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void Publish_OtherKeys_AreIgnored()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));
        var raised = false;
        interaction.LinkDeleteRequested += (_, _) => raised = true;

        interaction.Publish(new KeyEvent(InputKey.Escape));
        interaction.Publish(new KeyEvent(InputKey.Enter));

        Assert.IsFalse(raised);
    }

    // ── 候选集 ──────────────────────────────────────────────────────────────

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

    [TestMethod]
    public void For_SameTree_ReturnsTheSameHub()
    {
        // hub 只有一个位置：同一个树拿到的一定是同一个实例，适配器与宿主因此不需要各自持有。
        var tree = TreeWith(ReadyLink(0, 0, 100, 0));

        Assert.AreSame(LinkInteraction.For(tree), LinkInteraction.For(tree));
    }

    [TestMethod]
    public void Hover_DefaultHighlight_MovesBetweenLinks()
    {
        // 默认高亮：hub 自己把新的一条点亮、旧的一条灭掉。互斥因此是免费的 ——
        // 各家 demo 里那个「单选管理器」不再需要存在。
        var upper = new HighlightingVisual();
        var lower = new HighlightingVisual();
        var upperLink = ReadyLink(0, 0, 100, 0);
        var lowerLink = ReadyLink(0, 200, 100, 200);
        upperLink.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]), upper);
        lowerLink.PublishCurve(LinkCurve.FromPoints([0d, 100d], [200d, 200d]), lower);
        var interaction = new LinkInteraction(TreeWith(upperLink, lowerLink));

        interaction.Publish(new PointerEvent(PointerPhase.Moved, new Anchor(50, 0, 0)));
        Assert.IsTrue(upper.IsHighlighted);
        Assert.IsFalse(lower.IsHighlighted);

        interaction.Publish(new PointerEvent(PointerPhase.Moved, new Anchor(50, 200, 0)));
        Assert.IsFalse(upper.IsHighlighted);
        Assert.IsTrue(lower.IsHighlighted);
        Assert.AreEqual(2, upper.HighlightChanges);
    }

    [TestMethod]
    public void Hover_DefaultHighlight_TurnedOff_LeavesTheVisualAlone()
    {
        var visual = new HighlightingVisual();
        var link = ReadyLink(0, 0, 100, 0);
        link.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]), visual);
        var interaction = new LinkInteraction(TreeWith(link)) { AutoHighlight = false };

        interaction.Publish(new PointerEvent(PointerPhase.Moved, new Anchor(50, 0, 0)));

        Assert.IsFalse(visual.IsHighlighted);
        Assert.AreSame(link, interaction.HoveredLink);
    }

    [TestMethod]
    public void DeleteKey_DefaultPolicy_RemovesTheLink()
    {
        // 默认策略：hub 自己执行 DeleteCommand，所以生成出来的工程零代码就有删除。
        var tree = new TreeDefaultViewModel();
        var a = new NodeDefaultViewModel { Parent = tree };
        var b = new NodeDefaultViewModel { Parent = tree };
        var sa = new SlotDefaultViewModel { Parent = a };
        var sb = new SlotDefaultViewModel { Parent = b };
        a.Slots.Add(sa);
        b.Slots.Add(sb);
        tree.Nodes.Add(a);
        tree.Nodes.Add(b);
        var link = new LinkDefaultViewModel { Sender = sa, Receiver = sb, IsVisible = true };
        tree.Links.Add(link);
        tree.LinksMap[sa] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel> { [sb] = link };
        tree.GetHelper().VisibleItems.Add(link);
        link.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]));

        var interaction = LinkInteraction.For(tree);
        interaction.Publish(new PointerEvent(PointerPhase.Moved, new Anchor(50, 0, 0)));
        Assert.AreSame(link, interaction.HoveredLink);

        interaction.Publish(new KeyEvent(InputKey.Delete));

        Assert.IsFalse(tree.Links.Contains(link));
    }

    [TestMethod]
    public void DeleteKey_AutoDeleteOff_OnlyRequestsIt()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link)) { AutoDelete = false };
        interaction.Publish(new PointerEvent(PointerPhase.Moved, new Anchor(50, 0, 0)));
        var requested = false;
        interaction.LinkDeleteRequested += (_, _) => requested = true;

        interaction.Publish(new KeyEvent(InputKey.Delete));

        Assert.IsTrue(requested);
    }

    // 代表「连线视图」的可视对象：实现了 ILinkHighlight，所以 hub 能直接点亮/熄灭它。
    private sealed class HighlightingVisual : ILinkHighlight
    {
        private bool highlighted;

        public bool IsHighlighted
        {
            get => highlighted;
            set
            {
                if (highlighted == value) return;
                highlighted = value;
                HighlightChanges++;
            }
        }

        public int HighlightChanges { get; private set; }
    }
}
