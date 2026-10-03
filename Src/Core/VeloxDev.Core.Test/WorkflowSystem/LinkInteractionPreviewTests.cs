using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The Preview phase: a subscriber sees each action <b>before</b> the framework handles it and can refuse it for
/// that one event — without turning the surface-wide policy switches off.
/// </summary>
[TestClass]
public sealed class LinkInteractionPreviewTests : LinkInteractionTestBase
{
    // ── 删除 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PreviewDelete_PreventDefault_KeepsTheLinkAndReportsNothing()
    {
        var tree = DeletableTree(out var link);
        var interaction = LinkInteraction.For(tree);
        interaction.Publish(Move(50, 0));

        var requested = 0;
        interaction.PreviewLinkDeleteRequested += (_, e) => e.Handle.PreventDefault = true;
        interaction.LinkDeleteRequested += (_, _) => requested++;

        interaction.Publish(new KeyEvent(InputKey.Delete));

        Assert.IsTrue(tree.Links.Contains(link), "拒绝之后这条线还在");
        Assert.AreEqual(0, requested, "默认动作没跑，Outcome 也不该报");
    }

    [TestMethod]
    public void PreviewDelete_StopPropagation_DeletesButReportsNothing()
    {
        // 两个标志互相独立：不报 Outcome，但框架的默认动作照跑。
        var tree = DeletableTree(out var link);
        var interaction = LinkInteraction.For(tree);
        interaction.Publish(Move(50, 0));

        var requested = 0;
        interaction.PreviewLinkDeleteRequested += (_, e) => e.Handle.StopPropagation = true;
        interaction.LinkDeleteRequested += (_, _) => requested++;

        interaction.Publish(new KeyEvent(InputKey.Delete));

        Assert.IsFalse(tree.Links.Contains(link), "默认动作仍然删除");
        Assert.AreEqual(0, requested, "只是不报");
    }

    [TestMethod]
    public void PreviewDelete_NoSubscriber_DeletesAsItAlwaysDid()
    {
        var tree = DeletableTree(out var link);
        var interaction = LinkInteraction.For(tree);
        interaction.Publish(Move(50, 0));

        interaction.Publish(new KeyEvent(InputKey.Delete));

        Assert.IsFalse(tree.Links.Contains(link));
    }

    [TestMethod]
    public void PreviewDelete_PreventDefaultThenDeleteYourself_RemovesItOnce()
    {
        // 推荐用法：先否决，宿主问过之后再自己执行 —— 线只该消失一次（第二次执行打在一条已经不在树上的线上）。
        var tree = DeletableTree(out var link);
        var interaction = LinkInteraction.For(tree);
        interaction.Publish(Move(50, 0));

        var previews = 0;
        interaction.PreviewLinkDeleteRequested += (_, e) =>
        {
            previews++;
            e.Handle.PreventDefault = true;
            link.DeleteCommand.Execute(null);
        };

        interaction.Publish(new KeyEvent(InputKey.Delete));

        Assert.AreEqual(1, previews);
        Assert.IsFalse(tree.Links.Contains(link));
    }

    // ── 按下 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PreviewLinkPressed_PreventDefault_DoesNotSelectItOrReport()
    {
        var top = ReadyLink(0, 0, 100, 0);
        var below = ReadyLink(0, 50, 100, 50);
        var interaction = new LinkInteraction(TreeWith(top, below));
        interaction.Publish(Move(50, 0));
        Assert.AreSame(top, interaction.HoveredLink);

        var pressed = 0;
        interaction.PreviewLinkPressed += (_, e) => e.Handle.PreventDefault = true;
        interaction.LinkPressed += (_, _) => pressed++;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 50, 0), PointerButtonKind.Left));

        Assert.AreSame(top, interaction.HoveredLink, "否决了这一次按下，选中不该跟着走");
        Assert.AreEqual(0, pressed);
    }

    [TestMethod]
    public void PreviewLinkPressed_StopPropagation_SelectsButReportsNothing()
    {
        var top = ReadyLink(0, 0, 100, 0);
        var below = ReadyLink(0, 50, 100, 50);
        var interaction = new LinkInteraction(TreeWith(top, below));
        interaction.Publish(Move(50, 0));

        var pressed = 0;
        interaction.PreviewLinkPressed += (_, e) => e.Handle.StopPropagation = true;
        interaction.LinkPressed += (_, _) => pressed++;

        interaction.Publish(new PointerEvent(PointerPhase.Pressed, new Anchor(50, 50, 0), PointerButtonKind.Left));

        Assert.AreSame(below, interaction.HoveredLink, "默认动作照跑");
        Assert.AreEqual(0, pressed, "只是不报");
    }

    // ── 悬停 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PreviewHoverChanged_PreventDefault_KeepsTheOldHoverAndHighlight()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var visual = new HighlightingVisual();
        link.PublishCurve(LinkCurve.FromPoints([0d, 100d], [0d, 0d]), visual);
        var interaction = new LinkInteraction(TreeWith(link));
        interaction.Publish(Move(50, 0));
        Assert.IsTrue(visual.IsHighlighted);

        var changed = 0;
        interaction.PreviewHoverChanged += (_, e) => e.Handle.PreventDefault = true;
        interaction.HoverChanged += (_, _) => changed++;

        interaction.Publish(Move(500, 500));

        Assert.AreSame(link, interaction.HoveredLink, "hover 留在原处");
        Assert.IsTrue(visual.IsHighlighted, "高亮也不动");
        Assert.AreEqual(0, changed);
    }

    [TestMethod]
    public void PreviewHoverChanged_IsNotRaisedWhileTheHoverDoesNotChange()
    {
        // 同一条线上移动：不该反复问订阅方同一个问题（也挡住既有那条「每次移动只报一次」的回归）。
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        var previews = 0;
        interaction.PreviewHoverChanged += (_, _) => previews++;

        interaction.Publish(Move(50, 0));
        interaction.Publish(Move(60, 0));
        interaction.Publish(Move(70, 0));

        Assert.AreEqual(1, previews);
    }

    [TestMethod]
    public void PreviewAndOutcome_CarryTheSameHandle()
    {
        // Outcome 订阅方要能读到 Preview 的决定，所以一次动作只造一个句柄。
        var link = ReadyLink(0, 0, 100, 0);
        var interaction = new LinkInteraction(TreeWith(link));
        WorkflowEventHandle? fromPreview = null;
        WorkflowEventHandle? fromOutcome = null;
        interaction.PreviewHoverChanged += (_, e) => fromPreview = e.Handle;
        interaction.HoverChanged += (_, e) => fromOutcome = e.Handle;

        interaction.Publish(Move(50, 0));

        Assert.IsNotNull(fromPreview);
        Assert.AreSame(fromPreview, fromOutcome);
        Assert.IsFalse(fromOutcome!.IsDefaultPrevented);
    }
}
