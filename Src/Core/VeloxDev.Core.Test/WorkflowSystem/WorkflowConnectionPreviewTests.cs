using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Coverage for the connection-drag preview: the sender/candidate slots, the <see cref="SlotState"/> bits, the
/// virtual link's stage, the change event, and the geometry hit test adapters use to find a candidate.
/// </summary>
[TestClass]
public class WorkflowConnectionPreviewTests
{
    private static (TreeDefaultViewModel Tree, NodeDefaultViewModel Node, SlotDefaultViewModel Slot) NodeWithSlot(
        TreeDefaultViewModel? tree = null, double x = 0d, double y = 0d)
    {
        tree ??= new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);

        var slot = new SlotDefaultViewModel { Parent = node, Anchor = new Anchor(x, y, 0) };
        node.Slots.Add(slot);
        return (tree, node, slot);
    }

    [TestMethod]
    public void SendConnection_MarksTheSenderAndStartsTheVirtualLink()
    {
        var (tree, _, sender) = NodeWithSlot();

        tree.GetHelper().SendConnection(sender);

        var input = WorkflowInput.For(tree);
        Assert.AreSame(sender, input.ConnectionSender);
        Assert.IsNull(input.PreviewReceiver);
        Assert.AreEqual(ConnectionPreviewState.Started, input.PreviewState);
        Assert.IsTrue(sender.State.HasFlag(SlotState.PreviewSender),
            "the sender's preview bit must survive the state recompute");
        Assert.AreEqual(ConnectionPreviewState.Started, ((IConnectionPreviewLink)tree.VirtualLink).Preview);
    }

    [TestMethod]
    public void PreviewReceiver_IsMarkedAndReportedAsValid()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);

        var input = WorkflowInput.For(tree);
        Assert.AreSame(receiver, input.PreviewReceiver);
        Assert.AreEqual(ConnectionPreviewState.ValidTarget, input.PreviewState);
        Assert.IsTrue(receiver.State.HasFlag(SlotState.PreviewReceiver));
        Assert.AreEqual(ConnectionPreviewState.ValidTarget, ((IConnectionPreviewLink)tree.VirtualLink).Preview);
    }

    [TestMethod]
    public void PreviewReceiver_OnTheSameNode_IsReportedAsInvalid()
    {
        var (tree, node, sender) = NodeWithSlot();
        var receiver = new SlotDefaultViewModel { Parent = node };
        node.Slots.Add(receiver);

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);

        var input = WorkflowInput.For(tree);
        Assert.AreSame(receiver, input.PreviewReceiver);
        Assert.AreEqual(ConnectionPreviewState.InvalidTarget, input.PreviewState);
        Assert.IsTrue(receiver.State.HasFlag(SlotState.PreviewReceiver),
            "an invalid candidate is still the candidate under the pointer");
    }

    [TestMethod]
    public void PreviewReceiver_OnTheSenderItself_IsNotACandidate()
    {
        var (tree, _, sender) = NodeWithSlot();

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(sender);

        var input = WorkflowInput.For(tree);
        Assert.IsNull(input.PreviewReceiver);
        Assert.AreEqual(ConnectionPreviewState.Started, input.PreviewState);
        Assert.IsTrue(sender.State.HasFlag(SlotState.PreviewSender));
        Assert.IsFalse(sender.State.HasFlag(SlotState.PreviewReceiver));
    }

    [TestMethod]
    public void StandardCheckConnection_ReportsTheFirstRefusingRule()
    {
        var (tree, node, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);

        Assert.IsTrue(tree.StandardCheckConnection(sender, receiver).IsValid);

        var sameNode = new SlotDefaultViewModel { Parent = node };
        node.Slots.Add(sameNode);
        var sameNodeCheck = tree.StandardCheckConnection(sender, sameNode);
        Assert.IsFalse(sameNodeCheck.IsValid);
        Assert.AreEqual(ConnectionRejectReason.SameNode, sameNodeCheck.Reason);

        receiver.Channel = SlotChannel.OneTarget;
        Assert.AreEqual(
            ConnectionRejectReason.ReceiverCannotReceive,
            tree.StandardCheckConnection(sender, receiver).Reason);

        sender.Channel = SlotChannel.OneSource;
        Assert.AreEqual(
            ConnectionRejectReason.SenderCannotSend,
            tree.StandardCheckConnection(sender, receiver).Reason);
    }

    [TestMethod]
    public void PreviewValidating_CanTightenAFrameworkValidCandidate()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);
        var input = WorkflowInput.For(tree);
        var calls = 0;

        input.PreviewValidating += (_, e) =>
        {
            calls++;
            e.Reject("blocked by the host");
        };

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);

        Assert.AreEqual(1, calls);
        Assert.AreEqual(ConnectionPreviewState.InvalidTarget, input.PreviewState);
        Assert.IsTrue(input.PreviewCheck.HasValue);
        Assert.IsFalse(input.PreviewCheck.Value.IsValid);
        Assert.AreEqual(ConnectionRejectReason.RejectedByHost, input.PreviewCheck.Value.Reason);
        Assert.AreEqual("blocked by the host", input.PreviewCheck.Value.Message);
        Assert.AreEqual(ConnectionPreviewState.InvalidTarget, ((IConnectionPreviewLink)tree.VirtualLink).Preview);
    }

    [TestMethod]
    public void PreviewValidating_IsNotOfferedAFrameworkRejectedCandidate()
    {
        var (tree, node, sender) = NodeWithSlot();
        var sameNode = new SlotDefaultViewModel { Parent = node };
        node.Slots.Add(sameNode);
        var input = WorkflowInput.For(tree);
        var calls = 0;

        input.PreviewValidating += (_, _) => calls++;

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(sameNode);

        Assert.AreEqual(0, calls);
        Assert.AreEqual(ConnectionRejectReason.SameNode, input.PreviewCheck?.Reason);
        Assert.AreEqual(ConnectionPreviewState.InvalidTarget, input.PreviewState);
    }

    [TestMethod]
    public void RefreshConnectionPreview_ReevaluatesTheSameCandidate()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);
        var input = WorkflowInput.For(tree);
        var allow = false;
        var calls = 0;

        input.PreviewValidating += (_, e) =>
        {
            calls++;
            if (!allow) e.Reject("still checking");
        };

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(ConnectionPreviewState.InvalidTarget, input.PreviewState);

        // The same candidate must not re-consult the host on every pointer move.
        tree.StandardSetPreviewReceiver(receiver);
        Assert.AreEqual(1, calls);

        allow = true;
        tree.StandardRefreshConnectionPreview();

        Assert.AreEqual(2, calls);
        Assert.AreEqual(ConnectionPreviewState.ValidTarget, input.PreviewState);
        Assert.IsTrue(input.PreviewCheck?.IsValid);
    }

    [TestMethod]
    public void PreviewCheck_IsNullWhenThereIsNoCandidate()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);
        var input = WorkflowInput.For(tree);

        tree.GetHelper().SendConnection(sender);
        Assert.IsNull(input.PreviewCheck);
        Assert.AreEqual(ConnectionPreviewState.Started, input.PreviewState);

        tree.StandardSetPreviewReceiver(receiver);
        Assert.IsTrue(input.PreviewCheck?.IsValid);

        tree.StandardSetPreviewReceiver(null);
        Assert.IsNull(input.PreviewCheck);
        Assert.AreEqual(ConnectionPreviewState.Started, input.PreviewState);
    }

    [TestMethod]
    public void PreviewChanged_CarriesTheCheck()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);
        var input = WorkflowInput.For(tree);
        ConnectionCheck? seen = null;

        input.PreviewChanged += (_, e) => seen = e.Check;

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);

        Assert.IsTrue(seen.HasValue);
        Assert.IsTrue(seen.Value.IsValid);
        Assert.AreEqual(ConnectionRejectReason.None, seen.Value.Reason);
    }

    [TestMethod]
    public void MovingOffTheCandidate_ClearsItsPreviewBit()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);
        tree.StandardSetPreviewReceiver(null);

        var input = WorkflowInput.For(tree);
        Assert.IsNull(input.PreviewReceiver);
        Assert.AreEqual(ConnectionPreviewState.Started, input.PreviewState);
        Assert.IsFalse(receiver.State.HasFlag(SlotState.PreviewReceiver));
        Assert.IsNotNull(input.ConnectionSender, "leaving a candidate does not end the drag");
    }

    [TestMethod]
    public void ResettingTheVirtualLink_ClearsBothSides()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);
        tree.GetHelper().ResetVirtualLink();

        var input = WorkflowInput.For(tree);
        Assert.IsNull(input.ConnectionSender);
        Assert.IsNull(input.PreviewReceiver);
        Assert.AreEqual(ConnectionPreviewState.None, input.PreviewState);
        Assert.IsFalse(sender.State.HasFlag(SlotState.PreviewSender));
        Assert.IsFalse(receiver.State.HasFlag(SlotState.PreviewReceiver));
        Assert.AreEqual(ConnectionPreviewState.None, ((IConnectionPreviewLink)tree.VirtualLink).Preview);
    }

    [TestMethod]
    public void PreviewChanged_ReportsTheStagesInOrder()
    {
        var (tree, _, sender) = NodeWithSlot();
        var (_, _, receiver) = NodeWithSlot(tree, 140d, 0d);
        var states = new List<ConnectionPreviewState>();

        var input = WorkflowInput.For(tree);
        input.PreviewChanged += (_, e) => states.Add(e.State);

        tree.GetHelper().SendConnection(sender);
        tree.StandardSetPreviewReceiver(receiver);
        tree.StandardSetPreviewReceiver(null);
        tree.GetHelper().ResetVirtualLink();

        CollectionAssert.AreEqual(
            new[]
            {
                ConnectionPreviewState.Started,
                ConnectionPreviewState.ValidTarget,
                ConnectionPreviewState.Started,
                ConnectionPreviewState.None,
            },
            states);
    }

    [TestMethod]
    public void SlotHitTest_FindsTheNearestMeasuredSlot()
    {
        var (tree, _, first) = NodeWithSlot(x: 0d, y: 0d);
        var (_, _, second) = NodeWithSlot(tree, 100d, 0d);

        Assert.AreSame(first, SlotHitTestEx.HitTest(tree, 3d, 2d));
        Assert.AreSame(second, SlotHitTestEx.HitTest(tree, 98d, 4d));
        Assert.IsNull(SlotHitTestEx.HitTest(tree, 50d, 50d));
    }

    [TestMethod]
    public void SlotHitTest_SkipsAnUnmeasuredAnchor()
    {
        var (tree, _, slot) = NodeWithSlot();
        slot.Anchor = new Anchor(double.NaN, double.NaN, 0);

        Assert.IsNull(SlotHitTestEx.HitTest(tree, 0d, 0d));
    }
}
