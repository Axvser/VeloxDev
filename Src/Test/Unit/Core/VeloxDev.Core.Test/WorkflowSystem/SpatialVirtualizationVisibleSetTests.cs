using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Pins what a virtualization pass puts into <c>VisibleItems</c>: which items and in what order.
/// Order is the pool's materialization order, and a pass that reuses its working buffers must still
/// reproduce the same sequence when it returns to a viewport it has already visited.
/// </summary>
[TestClass]
public sealed class SpatialVirtualizationVisibleSetTests
{
    private const double CellSize = 100d;
    private const double NodeSize = 40d;

    [TestMethod]
    public void ReturningToTheSameViewportReproducesTheSameItemsInTheSameOrder()
    {
        var tree = Chain(6);
        var first = Snapshot(tree);

        tree.Virtualize(new Viewport(5000d, 5000d, 100d, 100d));
        Assert.HasCount(1, tree.GetHelper().VisibleItems, "a viewport away from every node leaves only VirtualLink");

        var second = Snapshot(tree);
        AssertSameSequence(first, second);
    }

    [TestMethod]
    public void DirectlyVisibleNodesKeepTheLinksThatReachOneHopOut()
    {
        var tree = Chain(6);
        tree.Virtualize(new Viewport(0d, 0d, 250d, 50d));

        var visible = Snapshot(tree);

        // 视口直接罩住 node0/node1，pair(1,2) 的并集也压到右边界 —— 扩张一层再把 node3 与它那条连线带进来。
        CollectionAssert.Contains(visible, tree.Nodes[0]);
        CollectionAssert.Contains(visible, tree.Nodes[1]);
        CollectionAssert.Contains(visible, tree.Nodes[2]);
        CollectionAssert.Contains(visible, tree.Nodes[3]);
        Assert.IsFalse(visible.Contains(tree.Nodes[4]), "one hop past the frontier is not expanded");
        CollectionAssert.Contains(visible, tree.Links[0]);
        CollectionAssert.Contains(visible, tree.Links[1]);
        CollectionAssert.Contains(visible, tree.Links[2]);
        Assert.IsFalse(visible.Contains(tree.Links[3]), "the link beyond the expanded node is not included");
        Assert.AreSame(tree.VirtualLink, visible[0], "VirtualLink always leads the sequence");
        Assert.HasCount(8, visible);
    }

    [TestMethod]
    public void TheInsetWidensOnlyTheQueryNotTheAuthoritativeViewport()
    {
        var tree = Isolated(0d, 250d, 400d);
        var viewport = new Viewport(0d, 0d, 250d, 100d);

        tree.Virtualize(viewport);
        var before = Snapshot(tree);
        CollectionAssert.Contains(before, tree.Nodes[0]);
        Assert.IsFalse(before.Contains(tree.Nodes[1]), "left edge exactly on the right edge does not intersect");
        Assert.IsFalse(before.Contains(tree.Nodes[2]));

        // 右边缘补 100：只把查询矩形向右长，节点属性与视口本身不动。
        tree.SetVirtualizeInset(right: 100d);
        tree.Virtualize(viewport);
        var after = Snapshot(tree);
        CollectionAssert.Contains(after, tree.Nodes[0]);
        CollectionAssert.Contains(after, tree.Nodes[1], "the inset pulls in the node that was just past the edge");
        Assert.IsFalse(after.Contains(tree.Nodes[2]), "the inset is 100 px, not unbounded");
    }

    private static List<IWorkflowComponent> Snapshot(TreeDefaultViewModel tree)
        => [.. tree.GetHelper().VisibleItems];

    private static void AssertSameSequence(
        IReadOnlyList<IWorkflowComponent> expected,
        IReadOnlyList<IWorkflowComponent> actual)
    {
        Assert.HasCount(expected.Count, actual);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.AreSame(expected[index], actual[index], $"visible item {index} moved or changed");
        }
    }

    // 6 个节点排成一行、相邻节点连线；所有节点与连线在开索引之前就位。
    private static TreeDefaultViewModel Chain(int count)
    {
        var tree = new TreeDefaultViewModel();
        var outputs = new ISlotComponent[count];
        for (var index = 0; index < count; index++)
        {
            var node = new NodeDefaultViewModel
            {
                Parent = tree,
                Anchor = new Anchor(index * 200d, 0d, 0),
                Size = new Size { Width = NodeSize, Height = NodeSize },
            };
            var output = new SlotDefaultViewModel { Parent = node, Channel = SlotChannel.MultipleBoth };
            node.Slots.Add(output);
            outputs[index] = output;
            tree.Nodes.Add(node);
        }

        for (var index = 0; index + 1 < count; index++)
        {
            tree.Links.Add(new LinkDefaultViewModel
            {
                Sender = outputs[index],
                Receiver = outputs[index + 1],
                IsVisible = true,
            });
        }

        tree.EnableMap(CellSize, tree.GetHelper().VisibleItems);
        return tree;
    }

    // 没有连线的一组节点，用来单独钉查询边界。
    private static TreeDefaultViewModel Isolated(params double[] columns)
    {
        var tree = new TreeDefaultViewModel();
        foreach (var column in columns)
        {
            tree.Nodes.Add(new NodeDefaultViewModel
            {
                Parent = tree,
                Anchor = new Anchor(column, 0d, 0),
                Size = new Size { Width = NodeSize, Height = NodeSize },
            });
        }

        tree.EnableMap(CellSize, tree.GetHelper().VisibleItems);
        return tree;
    }
}
