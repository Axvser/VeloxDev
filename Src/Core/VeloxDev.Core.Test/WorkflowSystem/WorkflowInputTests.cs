using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Tests for the input route: an adapter hands it a standard input event whose target it already resolved, and the
/// route decides who hears about it and applies the framework's own reaction.
/// </summary>
[TestClass]
public class WorkflowInputTests : WorkflowInputTestBase
{
    // ── 目标跟踪 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Route_EnteringALink_TracksItAsThePointerTarget()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var input = new WorkflowInput(TreeWith(link));

        input.Route(Enter(50, 0, link));

        Assert.AreSame(link, input.PointerTarget);
        Assert.AreSame(link, input.HoveredLink);
    }

    [TestMethod]
    public void Route_MovingOffEveryLink_ClearsTheTarget()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var input = new WorkflowInput(TreeWith(link));
        input.Route(Move(50, 0, link));

        input.Route(Move(50, 500));

        Assert.IsNull(input.PointerTarget);
    }

    [TestMethod]
    public void Route_PointerLeavingTheSurface_ClearsTheTarget()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var input = new WorkflowInput(TreeWith(link));
        input.Route(Move(50, 0, link));

        input.Route(Exit(50, 0));

        Assert.IsNull(input.PointerTarget);
    }

    [TestMethod]
    public void Route_Suspended_LeavesTheTargetAlone()
    {
        // 宿主的菜单开着时指针在菜单上：那段时间的移动与离开都不该把菜单针对的那条线取消选中。
        var link = ReadyLink(0, 0, 100, 0);
        var input = new WorkflowInput(TreeWith(link));
        input.Route(Move(50, 0, link));
        input.IsSuspended = true;

        input.Route(Move(50, 500));
        input.Route(Exit(50, 500));

        Assert.AreSame(link, input.PointerTarget);
    }

    [TestMethod]
    public void Route_LeavingALink_TellsItBeforeItTellsTheNextOne()
    {
        // 逐组件订阅要能自己听到「我这条线不再被指着了」：路由先给留下那个发一次 Exited，再发新的那条。
        var left = ReadyLink(0, 0, 100, 0);
        var entered = ReadyLink(0, 200, 100, 200);
        var tree = TreeWith(left, entered);
        var input = WorkflowInput.For(tree);
        input.Route(Enter(50, 0, left));

        var events = new List<string>();
        Events(left).Input.PointerExited += (_, _) => events.Add("left-exited");
        Events(entered).Input.PointerEntered += (_, _) => events.Add("entered-entered");

        input.Route(Move(50, 200, entered));

        CollectionAssert.AreEqual(new[] { "left-exited", "entered-entered" }, events);
    }

    [TestMethod]
    public void Route_MovingOffEveryLink_TellsTheOneItLeft()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var input = new WorkflowInput(TreeWith(link));
        input.Route(Move(50, 0, link));

        var exited = 0;
        Events(link).Input.PointerExited += (_, _) => exited++;

        input.Route(Move(500, 500));

        Assert.AreEqual(1, exited);
    }

    [TestMethod]
    public void Route_TracksWhateverTargetTheAdapterResolved()
    {
        // 命中是适配器的事：它说指到了哪条线就是哪条线，Core 不再自己按坐标判一次。
        var link = ReadyLink(0, 0, 100, 0);
        var input = new WorkflowInput(TreeWith(link));

        input.Route(Move(5000, 5000, link));

        Assert.AreSame(link, input.PointerTarget);
    }

    // ── 键：只路由，不做事 ──────────────────────────────────────────────────

    [TestMethod]
    public void Route_KeyDown_ActsOnNothingItself()
    {
        // 路由没有默认动作：删除是宿主订 KeyDown 自己做的（与高亮、菜单同一条路）。
        var tree = DeletableTree(out var link);

        WorkflowInput.For(tree).Route(Down(InputKey.Delete, link));

        Assert.IsTrue(tree.Links.Contains(link));
    }

    [TestMethod]
    public void Route_KeyDown_ReachesTheTargetSoAHostCanDelete()
    {
        var tree = DeletableTree(out var link);
        Events(link).Input.KeyDown += (_, e) =>
        {
            if (e.Key == InputKey.Delete) link.DeleteCommand.Execute(null);
        };

        WorkflowInput.For(tree).Route(Down(InputKey.Delete, link));

        Assert.IsFalse(tree.Links.Contains(link));
    }

    [TestMethod]
    public void Route_KeyDown_PreventDefault_IsReadByTheHandlerThatActs()
    {
        // 框架没有「默认那一手」可挡了，所以句柄由**动手的那个订阅方**自己查 —— 这正是七家菜单接线里
        // 对 PointerPressed 的同一种写法（先查 PreventDefault，再弹）。
        var tree = DeletableTree(out var link);
        Events(link).Input.KeyDown += (_, e) => e.Handle.PreventDefault = true;
        Events(tree).Input.KeyDown += (_, e) =>
        {
            if (e.Handle.PreventDefault) return;
            link.DeleteCommand.Execute(null);
        };

        WorkflowInput.For(tree).Route(Down(InputKey.Delete, link));

        Assert.IsTrue(tree.Links.Contains(link));
    }


    // ── 落点 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void For_SameTree_ReturnsTheSameRoute()
    {
        // 只有一个位置：同一棵树拿到的一定是同一个实例，适配器与宿主因此不需要各自持有。
        var tree = TreeWith(ReadyLink(0, 0, 100, 0));

        Assert.AreSame(WorkflowInput.For(tree), WorkflowInput.For(tree));
    }

    [TestMethod]
    public void Route_EmptyCanvas_StillReachesTheTree()
    {
        // 空白画布上的输入不落在任何组件上，但树自己还是要听得到（宿主的画布手势就从这里接）。
        var tree = TreeWith(ReadyLink(0, 0, 100, 0));
        var heard = 0;
        ((IInputEvents)tree.GetHelper()).Input.PointerPressed += (_, _) => heard++;

        WorkflowInput.For(tree).Route(Press(500, 500, MouseButton.Right));

        Assert.AreEqual(1, heard);
    }
}
