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

    [TestMethod]
    public void DeleteNodeWithConnections_UndoOnceRestoresLinksAndBookkeeping()
    {
        var tree = new TreeDefaultViewModel();
        var removed = new NodeDefaultViewModel();
        var kept = new NodeDefaultViewModel();
        var sender = new SlotDefaultViewModel { Channel = SlotChannel.MultipleBoth };
        var receiver = new SlotDefaultViewModel { Channel = SlotChannel.MultipleBoth };

        tree.GetHelper().CreateNode(removed);
        tree.GetHelper().CreateNode(kept);
        removed.GetHelper().CreateSlot(sender);
        kept.GetHelper().CreateSlot(receiver);
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
        tree.GetHelper().ClearHistory();

        removed.GetHelper().Delete();

        Assert.IsEmpty(tree.Links, "the connection goes with the node");
        Assert.IsEmpty(sender.Targets);
        Assert.IsEmpty(receiver.Sources);
        Assert.IsFalse(tree.LinksMap.ContainsKey(sender));

        tree.GetHelper().Undo();

        Assert.HasCount(2, tree.Nodes, "the node is back");
        Assert.HasCount(1, tree.Links, "the connection is back");
        Assert.AreSame(removed, sender.Parent);
        Assert.IsTrue(sender.Targets.Contains(receiver), "the sender-side relation is restored");
        Assert.IsTrue(receiver.Sources.Contains(sender), "the receiver-side relation is restored");
        Assert.IsTrue(tree.LinksMap.TryGetValue(sender, out var map) && map.ContainsKey(receiver),
            "the lookup map is restored");
    }
}
