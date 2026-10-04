using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Tests for the contract a routed input event carries: one argument instance and one
/// <see cref="WorkflowEventHandle"/> travel the whole route, the target hears it before its ancestors, and the two
/// flags steer the rest of the route (and let a handler that would have acted stand down).
/// </summary>
[TestClass]
public class WorkflowInputHandleTests : WorkflowInputTestBase
{
    [TestMethod]
    public void Route_TheTargetHearsAKeyBeforeItsAncestors()
    {
        // 顺序由构造保证：目标是第一级的订阅方，树在后 —— 所以「更靠前的一级说不」时，后面那个还来得及看句柄。
        var tree = DeletableTree(out var link);
        var order = new List<string>();
        Events(link).Input.KeyDown += (_, _) => order.Add("link");
        Events(tree).Input.KeyDown += (_, _) => order.Add("tree");

        WorkflowInput.For(tree).Route(Down(WorkflowKey.Delete, link));

        CollectionAssert.AreEqual(new[] { "link", "tree" }, order);
    }

    [TestMethod]
    public void Route_OneHandleReachesEveryElementOfTheRoute()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(link);
        WorkflowEventHandle? onLink = null;
        WorkflowEventHandle? onTree = null;
        Events(link).Input.PointerPressed += (_, e) => onLink = e.Handle;
        Events(tree).Input.PointerPressed += (_, e) => onTree = e.Handle;

        WorkflowInput.For(tree).Route(Press(50, 0, WorkflowMouseButton.Right, link));

        Assert.IsNotNull(onLink);
        Assert.AreSame(onLink, onTree, "一次动作只有一个句柄");
    }

    [TestMethod]
    public void Route_StopPropagationOnTheTarget_KeepsAncestorsFromHearingIt()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(link);
        var onTree = 0;
        Events(link).Input.PointerPressed += (_, e) => e.Handle.StopPropagation = true;
        Events(tree).Input.PointerPressed += (_, _) => onTree++;

        WorkflowInput.For(tree).Route(Press(50, 0, WorkflowMouseButton.Right, link));

        Assert.AreEqual(0, onTree);
    }

    [TestMethod]
    public void Route_PreventDefault_HoldsOnlyTheFrameworkReaction_NotTheRoute()
    {
        // 两个标志管两件事：PreventDefault 挡住框架自己那一手，StopPropagation 才截断传递。
        var link = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(link);
        var onTree = 0;
        Events(link).Input.PointerPressed += (_, e) => e.Handle.PreventDefault = true;
        Events(tree).Input.PointerPressed += (_, _) => onTree++;

        WorkflowInput.For(tree).Route(Press(50, 0, WorkflowMouseButton.Right, link));

        Assert.AreEqual(1, onTree);
    }

    [TestMethod]
    public void Route_BubblesFromASlotThroughItsNodeToTheTree()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel { Parent = tree };
        var a = new NodeDefaultViewModel { Parent = tree };
        var slot = new SlotDefaultViewModel { Parent = node };
        var other = new SlotDefaultViewModel { Parent = a };
        node.Slots.Add(slot);
        a.Slots.Add(other);
        tree.Nodes.Add(node);
        tree.Nodes.Add(a);

        var order = new List<string>();
        Events(slot).Input.PointerPressed += (_, _) => order.Add("slot");
        Events(node).Input.PointerPressed += (_, _) => order.Add("node");
        Events(tree).Input.PointerPressed += (_, _) => order.Add("tree");

        WorkflowInput.For(tree).Route(Press(0, 0, WorkflowMouseButton.Left, slot));

        CollectionAssert.AreEqual(new[] { "slot", "node", "tree" }, order);
    }

    [TestMethod]
    public void Route_AncestorSeesTheOriginalTarget_NotItself()
    {
        // 冒泡传的是同一个 args：祖先看到的 target 是「谁被指到了」，不是它自己。
        var link = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(link);
        IWorkflowViewModel? seen = null;
        Events(tree).Input.PointerPressed += (_, e) => seen = e.Target;

        WorkflowInput.For(tree).Route(Press(50, 0, WorkflowMouseButton.Right, link));

        Assert.AreSame(link, seen);
    }

    [TestMethod]
    public void Route_CarriesTheSourceViewAndTheLayer()
    {
        // 指针转成 Anchor 时把来源视图所在图层带成 Z —— 订阅方据此知道输入是从哪一层来的。
        var link = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(link);
        object? source = null;
        var layer = -1;
        Events(link).Input.PointerMoved += (_, e) => { source = e.Source; layer = e.Position.Layer; };

        WorkflowInput.For(tree).Route(Move(50, 0, link, layer: -100));

        Assert.IsInstanceOfType<SourceView>(source);
        Assert.AreEqual(-100, layer);
    }

    [TestMethod]
    public void Route_PressCarriesTheButtonAndTheModifiers()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(link);
        var button = WorkflowMouseButton.None;
        var modifiers = InputModifiers.None;
        Events(link).Input.PointerPressed += (_, e) => { button = e.Button; modifiers = e.Modifiers; };

        var args = new WorkflowPointerPressedEventArgs(
            new Anchor(50, 0, 0), InputModifiers.Control, new SourceView(), link,
            WorkflowMouseButton.Middle, 2, new WorkflowEventHandle());
        WorkflowInput.For(tree).Route(args);

        Assert.AreEqual(WorkflowMouseButton.Middle, button);
        Assert.AreEqual(InputModifiers.Control, modifiers);
    }
}
