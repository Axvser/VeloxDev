using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Verifies workflow mutations produce exactly the expected number of undo entries.
/// An Agent driving the workflow must not force the user to press undo many times for
/// a single logical operation (e.g. deleting a slot with N connections used to be N+1 entries).
/// </summary>
[TestClass]
public class WorkflowUndoCountTests
{
    [TestMethod]
    public void DeleteSlotWithConnections_UndoOnceRestoresSlotAndConnections()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        var otherNode = new NodeDefaultViewModel();
        var slot = new SlotDefaultViewModel { Channel = SlotChannel.MultipleBoth };
        var otherSlot = new SlotDefaultViewModel { Channel = SlotChannel.MultipleBoth };

        tree.GetHelper().CreateNode(node);
        tree.GetHelper().CreateNode(otherNode);
        node.GetHelper().CreateSlot(slot);
        otherNode.GetHelper().CreateSlot(otherSlot);
        tree.GetHelper().SendConnection(slot);
        tree.GetHelper().ReceiveConnection(otherSlot);
        Assert.HasCount(1, tree.Links);

        tree.GetHelper().ClearHistory(); // isolate the delete+undo cycle

        slot.GetHelper().Delete(); // delete a slot that owns 1 connection

        Assert.IsEmpty(node.Slots, "slot should be removed from the node");
        Assert.IsEmpty(tree.Links, "its connection should be removed too");

        // ONE undo must restore BOTH the slot and the connection (single atomic entry,
        // not one entry per link-delete plus one for the slot removal).
        tree.GetHelper().Undo();
        Assert.HasCount(1, node.Slots, "one undo restores the slot");
        Assert.HasCount(1, tree.Links, "one undo restores the connection");
        Assert.IsTrue(ReferenceEquals(slot, node.Slots[0]), "one undo restores the same slot instance");
    }
}
