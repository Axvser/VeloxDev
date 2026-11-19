using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// A selection is the rectangle a drag described, answered from the same spatial index virtualization
/// uses — so it has to agree with what is on screen, in the frame the screen is laid out in.
/// </summary>
/// <remarks>
/// The two-point overload exists because building the rectangle and getting its frame right is the part a
/// host gets wrong: the corners arrive in whichever order the drag went, a drag that never moved describes
/// no area, and the frame is the collapsed one rather than the stored one — those differ by the zoom
/// factor, so a rectangle built in the wrong one is only correct at 100%.
/// </remarks>
[TestClass]
public sealed class WorkflowSpatialExTests
{
    private const double NodeSize = 40d;
    private const double CellSize = 100d;

    [TestMethod]
    public void NodesInsideTheRectangleComeBack()
    {
        var tree = Tree((0, 0), (300, 0), (0, 300));
        var inside = tree.Nodes[0];

        var picked = tree.QueryNodes(new Anchor(-10, -10, 0), new Anchor(50, 50, 0));

        Assert.HasCount(1, picked);
        Assert.AreSame(inside, picked[0]);
    }

    [TestMethod]
    public void CornersInEitherOrderDescribeTheSameRectangle()
    {
        var tree = Tree((150, 150));
        var node = tree.Nodes[0];

        // 拖动可以朝四个方向中的任何一个，选中的东西不该因此不同。
        foreach (var (from, to) in new[]
        {
            (new Anchor(100, 100, 0), new Anchor(200, 200, 0)),
            (new Anchor(200, 200, 0), new Anchor(100, 100, 0)),
            (new Anchor(200, 100, 0), new Anchor(100, 200, 0)),
            (new Anchor(100, 200, 0), new Anchor(200, 100, 0)),
        })
        {
            var picked = tree.QueryNodes(from, to);
            Assert.HasCount(1, picked);
            Assert.AreSame(node, picked[0]);
        }
    }

    [TestMethod]
    public void ADragThatNeverMovedSelectsNothing()
    {
        var tree = Tree((0, 0));

        // 点下去没动就松开：描述不了面积。空结果 —— 不是零尺寸的查询，更不是错误。
        Assert.IsEmpty(tree.QueryNodes(new Anchor(10, 10, 0), new Anchor(10, 10, 0)));

        // 只在一个方向上没动也一样。
        Assert.IsEmpty(tree.QueryNodes(new Anchor(10, 10, 0), new Anchor(90, 10, 0)));
        Assert.IsEmpty(tree.QueryNodes(new Anchor(10, 10, 0), new Anchor(10, 90, 0)));
    }

    [TestMethod]
    public void TouchingANodeIsEnoughItDoesNotHaveToBeEnclosed()
    {
        var tree = Tree((0, 0));   // 占 (0,0)–(40,40)

        // 只搭到右下角：算选中。框选是「碰到就选」，不是「完全包住才选」。
        Assert.HasCount(1, tree.QueryNodes(new Anchor(30, 30, 0), new Anchor(80, 80, 0)));

        // 完全错开：不选。
        Assert.IsEmpty(tree.QueryNodes(new Anchor(41, 41, 0), new Anchor(80, 80, 0)));
    }

    [TestMethod]
    public void ASelectionSpanningSeveralCellsFindsEveryNodeInThem()
    {
        var tree = Tree((0, 0), (300, 300), (600, 600));

        // 索引按格分桶，跨格的框选必须把每一格里命中的都收齐 —— 手写一遍全遍历也能对，
        // 但那会和索引对不上，而索引才是屏幕上的那一份。
        var picked = tree.QueryNodes(new Anchor(-1, -1, 0), new Anchor(700, 700, 0));

        Assert.HasCount(3, picked);
        foreach (var node in tree.Nodes)
        {
            Assert.IsTrue(picked.Contains(node), "every node inside the rectangle has to come back");
        }
    }

    [TestMethod]
    public void NodesOutsideTheRectangleAreLeftOut()
    {
        var tree = Tree((100, 100), (900, 900), (-500, 100));
        var inside = tree.Nodes[0];

        var picked = tree.QueryNodes(new Anchor(0, 0, 0), new Anchor(300, 300, 0));

        Assert.HasCount(1, picked);
        Assert.AreSame(inside, picked[0]);
    }

    [TestMethod]
    public void TheRectangleIsInTheFrameNodesAreReadIn_NotTheOneTheyAreStoredIn()
    {
        var tree = Tree((200, 200));
        var node = tree.Nodes[0];

        // 存的是 200，而 getter 除以 Scale —— 0.5 缩放下节点画在 400。
        tree.Layout.Scale = new Scale(0.5, 0.5);
        Assert.AreEqual(400d, node.Anchor.Horizontal, 0.001,
            "precondition: a node's Anchor getter returns the collapsed frame, and the index follows it");

        // 指针位置也是折叠帧，所以拿两个指针点框选是对的。
        Assert.HasCount(1, tree.QueryNodes(new Anchor(350, 350, 0), new Anchor(450, 450, 0)));

        // 拿存下来的那对数字当坐标就选不中。这正是这个重载存在的理由：把帧的选择收进库，
        // 而不是让每个宿主各踩一次 —— 而且这个错在 100% 缩放下看不出来。
        Assert.IsEmpty(tree.QueryNodes(new Anchor(150, 150, 0), new Anchor(250, 250, 0)));
    }

    [TestMethod]
    public void QueryingWithoutASpatialMapReportsWhatIsMissing()
    {
        var tree = new TreeDefaultViewModel();
        tree.Nodes.Add(new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(0, 0, 0),
            Size = new Size { Width = NodeSize, Height = NodeSize },
        });

        // 没启用空间索引不该静默返回空 —— 那会把「忘了启用」读成「这里什么都没有」。
        Assert.ThrowsExactly<ArgumentNullException>(
            () => tree.QueryNodes(new Anchor(0, 0, 0), new Anchor(500, 500, 0)));
    }

    // 摆几个节点：左上角在 (left, top)，边长 NodeSize。节点先摆好再开索引，建表时就会把它们收进去。
    private static TreeDefaultViewModel Tree(params (double Left, double Top)[] positions)
    {
        var tree = new TreeDefaultViewModel();
        foreach (var (left, top) in positions)
        {
            tree.Nodes.Add(new NodeDefaultViewModel
            {
                Parent = tree,
                Anchor = new Anchor(left, top, 0),
                Size = new Size { Width = NodeSize, Height = NodeSize },
            });
        }

        // 空间索引不是默认开的 —— 只有带 cellSize 的 TreeHelper 才开。这里直接开而不是换 helper：
        // TreeHelper(cellSize) 会顺带启动 TickManager 的全局心跳，那是测试不该带来的副作用。
        tree.EnableMap(CellSize, tree.GetHelper().VisibleItems);
        return tree;
    }
}
