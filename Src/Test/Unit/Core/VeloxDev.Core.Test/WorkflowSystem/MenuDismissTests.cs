using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Tests for the two building blocks a host's context menu uses now that the link hub is gone: the tree keeps
/// telling who left (so a menu cannot outlive the link it acts on), and the input route keeps its hands off the
/// pointer while the menu is open.
/// </summary>
[TestClass]
public class MenuDismissTests : WorkflowInputTestBase
{
    [TestMethod]
    public void LinkRemoved_TellsAMenuWatcherThatItsLinkIsGone()
    {
        // 「菜单不能比它指着的那条线活得久」：宿主订这一个既有事件，比对是不是自己那条。
        var tree = DeletableTree(out var link);
        var closed = 0;
        (tree.GetHelper()).LinkRemoved += (_, removed) => { if (ReferenceEquals(removed, link)) closed++; };

        link.DeleteCommand.Execute(null);

        Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public void LinkRemoved_FiresOnlyForTheLinkThatLeft()
    {
        var tree = DeletableTree(out var link);
        var other = ReadyLink(0, 200, 100, 200);
        tree.Links.Add(other);
        var left = new List<IWorkflowLinkViewModel>();
        (tree.GetHelper()).LinkRemoved += (_, removed) => left.Add(removed);

        // 走集合这条路：谁离开就报谁，另一条不在名单里。
        tree.Links.Remove(link);

        CollectionAssert.AreEqual(new[] { link }, left);
    }

    [TestMethod]
    public void Route_Suspended_WaitsOutTheMenusOwnPointerTraffic()
    {
        // 菜单一开，指针就飞到菜单上：那段时间的移动与离开都不该改指针目标。
        var link = ReadyLink(0, 0, 100, 0);
        var input = new WorkflowInput(TreeWith(link));
        input.Route(Move(50, 0, link));
        input.IsSuspended = true;

        input.Route(Move(900, 900));
        input.Route(Exit(900, 900));

        Assert.AreSame(link, input.PointerTarget);

        input.IsSuspended = false;
        input.Route(Move(900, 900));

        Assert.IsNull(input.PointerTarget);
    }
}
