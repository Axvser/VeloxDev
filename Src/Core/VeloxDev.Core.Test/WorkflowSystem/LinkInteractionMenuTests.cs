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
}
