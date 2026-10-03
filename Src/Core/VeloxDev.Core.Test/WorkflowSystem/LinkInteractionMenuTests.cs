using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The context-menu contract: a right press is announced with a veto, and the host reports back whether its menu
/// opened or closed — which is what keeps the hover suspended while the pointer is on the menu.
/// </summary>
[TestClass]
public sealed class LinkInteractionMenuTests : LinkInteractionTestBase
{
    [TestMethod]
    public void ContextMenuRequested_RightPressOnALink_CarriesTheLinkAndTheSpot()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        ContextMenuRequestedEventArgs? seen = null;
        interaction.ContextMenuRequested += (_, e) => seen = e;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 0, 0), PointerButtonKind.Right));

        Assert.IsNotNull(seen);
        Assert.AreSame(link, seen!.Link);
        Assert.AreEqual(50d, seen.Position.Horizontal, 1e-9);
    }

    [TestMethod]
    public void ContextMenuRequested_RightPressOnEmptyCanvas_CarriesNoLink()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        ContextMenuRequestedEventArgs? seen = null;
        interaction.ContextMenuRequested += (_, e) => seen = e;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(500, 500, 0), PointerButtonKind.Right));

        Assert.IsNotNull(seen);
        Assert.IsNull(seen!.Link);
    }

    [TestMethod]
    public void ContextMenuRequesting_PreventDefault_SuppressesTheRequestToo()
    {
        // 菜单的「默认动作」是订阅方自己去弹，所以它需要一个 Preview 相：否决发生在这一相，
        // 弹出那一相根本不会来 —— 不再依赖「谁先订阅」，也就不会出现否决晚于弹出的那颗雷。
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        var requested = 0;
        var requesting = 0;
        WorkflowEventHandle? seen = null;
        interaction.ContextMenuRequesting += (_, e) =>
        {
            requesting++;
            seen = e.Handle;
            e.Handle.PreventDefault = true;
        };
        interaction.ContextMenuRequested += (_, _) => requested++;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 0, 0), PointerButtonKind.Right));

        Assert.AreEqual(1, requesting);
        Assert.AreEqual(0, requested, "被拒绝之后弹出那一相不报");
        Assert.IsTrue(seen!.IsDefaultPrevented);
    }

    [TestMethod]
    public void ContextMenuRequesting_NoSubscriber_StillRaisesRequested()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        var requested = 0;
        interaction.ContextMenuRequested += (_, _) => requested++;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 0, 0), PointerButtonKind.Right));

        Assert.AreEqual(1, requested);
    }

    [TestMethod]
    public void ContextMenuRequested_LeftPress_IsNotRaised()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        var raised = 0;
        interaction.ContextMenuRequested += (_, _) => raised++;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 0, 0), PointerButtonKind.Left));

        Assert.AreEqual(0, raised);
    }

    [TestMethod]
    public void ContextMenuRequested_PreventDefault_StillRaisesLinkPressed()
    {
        // 向后兼容：六个 demo 是从 LinkPressed 里弹菜单的，压掉它会让它们一按弹两次。
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        var pressed = 0;
        interaction.ContextMenuRequested += (_, e) => e.Handle.PreventDefault = true;
        interaction.LinkPressed += (_, _) => pressed++;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 0, 0), PointerButtonKind.Right));

        Assert.AreEqual(1, pressed);
    }

    [TestMethod]
    public void Publish_MenuOpened_SuspendsTheHoverAndRaisesOpened()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));
        ContextMenuOpenedEventArgs? opened = null;
        interaction.ContextMenuOpened += (_, e) => opened = e;

        interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, new Anchor(50, 0, 0), link));

        Assert.IsTrue(interaction.IsSuspended);
        Assert.IsNotNull(opened);
        Assert.AreSame(link, opened!.Link);

        // 菜单开着时指针飞到菜单上去了：那之后的**移动**与**离开**都不该把这次选中清掉
        //（离开这一路是 Jalium 那家实测逼出来的：Exited 原来不认挂起，菜单一开高亮就没了）。
        interaction.Publish(Move(500, 500));
        Assert.AreSame(link, interaction.HoveredLink);
        interaction.Publish(new PointerEvent(PointerPhase.Exited, new Anchor(500, 500, 0)));
        Assert.AreSame(link, interaction.HoveredLink, "挂起时 Exited 也不清选中");
    }

    [TestMethod]
    public void Publish_MenuClosed_ReleasesTheHoverAndRaisesClosed()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));
        interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, new Anchor(50, 0, 0), link));
        ContextMenuClosedEventArgs? closed = null;
        interaction.ContextMenuClosed += (_, e) => closed = e;

        interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, new Anchor(50, 0, 0), link));

        Assert.IsFalse(interaction.IsSuspended);
        Assert.IsNotNull(closed);
        Assert.AreSame(link, closed!.Link);

        // 解除挂起之后，移动又能改 hover。
        interaction.Publish(Move(500, 500));
        Assert.IsNull(interaction.HoveredLink);
    }

    [TestMethod]
    public void ContextMenuDismissRequested_LinkLeavesTheTreeWhileItsMenuIsOpen_AsksTheHostToClose()
    {
        // 「树」与「打开的那份菜单」同时知道的只有 hub，所以判定只能在这一处；菜单是宿主的弹窗，
        // 这里收不了，只请宿主收。
        var tree = DeletableTree(out var link);
        var interaction = new LinkInteraction(tree);
        interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, new Anchor(0, 0, 0), link));
        ContextMenuDismissRequestedEventArgs? seen = null;
        interaction.ContextMenuDismissRequested += (_, e) => seen = e;

        tree.Links.Remove(link);

        Assert.IsNotNull(seen);
        Assert.AreSame(link, seen!.Link);

        // 挂起不在这里放开：宿主收起菜单后照常报 Closed，放开仍然只有 hub 一个责任人。
        Assert.IsTrue(interaction.IsSuspended, "请宿主收菜单，不等于替它收");
    }

    [TestMethod]
    public void ContextMenuDismissRequested_AnotherLinkLeaves_DoesNotAsk()
    {
        var tree = DeletableTree(out var link);
        var a = tree.Nodes[0];
        var b = tree.Nodes[1];
        var other = new LinkDefaultViewModel { Sender = a.Slots[0], Receiver = b.Slots[0], IsVisible = true };
        tree.Links.Add(other);

        var interaction = new LinkInteraction(tree);
        interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, new Anchor(0, 0, 0), link));
        var asked = 0;
        interaction.ContextMenuDismissRequested += (_, _) => asked++;

        tree.Links.Remove(other);

        Assert.AreEqual(0, asked, "菜单指着的是 link，别的线离开与它无关");
    }

    [TestMethod]
    public void ContextMenuDismissRequested_MenuAlreadyClosed_DoesNotAsk()
    {
        var tree = DeletableTree(out var link);
        var interaction = new LinkInteraction(tree);
        interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, new Anchor(0, 0, 0), link));
        interaction.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, new Anchor(0, 0, 0), link));
        var asked = 0;
        interaction.ContextMenuDismissRequested += (_, _) => asked++;

        tree.Links.Remove(link);

        Assert.AreEqual(0, asked, "菜单已经收起了，没有要收的东西");
    }
}
