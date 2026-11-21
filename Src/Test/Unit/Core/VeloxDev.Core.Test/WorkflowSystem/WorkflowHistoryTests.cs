using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class WorkflowHistoryTests
{
    [TestMethod]
    public void CreateNode_UndoTwice_DoesNotRestoreNode()
    {
        var tree = new TreeDefaultComponent();
        var node = new NodeDefaultComponent();

        tree.GetHelper().CreateNode(node);
        Assert.HasCount(1, tree.Nodes);

        tree.GetHelper().Undo();
        Assert.IsEmpty(tree.Nodes);

        tree.GetHelper().Undo();
        Assert.IsEmpty(tree.Nodes);
        Assert.IsNull(node.Parent);
    }

    [TestMethod]
    public void CreateNode_UndoAndRedo_PreservesNodeSlots()
    {
        var tree = new TreeDefaultComponent();
        var node = new NodeDefaultComponent();
        var slot = new SlotDefaultComponent();
        node.GetHelper().CreateSlot(slot);

        tree.GetHelper().CreateNode(node);
        tree.GetHelper().Undo();

        Assert.AreSame(node, slot.Parent);
        Assert.HasCount(1, node.Slots);

        tree.GetHelper().Redo();

        Assert.HasCount(1, tree.Nodes);
        Assert.AreSame(tree, node.Parent);
        Assert.AreSame(node, slot.Parent);
        Assert.HasCount(1, node.Slots);
    }

    [TestMethod]
    public void CreateSlot_UndoTwice_DoesNotRestoreSlot()
    {
        var tree = new TreeDefaultComponent();
        var node = new NodeDefaultComponent();
        tree.GetHelper().CreateNode(node);
        tree.GetHelper().ClearHistory();

        var slot = new SlotDefaultComponent();
        node.GetHelper().CreateSlot(slot);
        Assert.HasCount(1, node.Slots);

        tree.GetHelper().Undo();
        Assert.IsEmpty(node.Slots);

        tree.GetHelper().Undo();
        Assert.IsEmpty(node.Slots);
        Assert.IsNull(slot.Parent);
    }
}
