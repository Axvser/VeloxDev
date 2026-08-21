using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Unit tests for the link render-ready contract driven by core NaN value checks.
///
/// Contract: the link is visible and both endpoint anchors are in place — a slot's anchor defaults its
/// horizontal/vertical coordinates to <see cref="double.NaN"/> (no value / awaiting GUI measurement), and is only
/// released after real GUI measurement writes non-NaN coordinates.
/// The tests set slot.Anchor directly to simulate GUI measurement; they rely on no events, timestamps, or GUI notifications.
/// </summary>
[TestClass]
public class WorkflowLinkRenderReadyTests
{
    private static IWorkflowNodeViewModel NodeWithSlots(params IWorkflowSlotViewModel[] slots)
    {
        var node = new NodeDefaultViewModel();
        foreach (var slot in slots)
        {
            slot.Parent = node;
            node.Slots.Add(slot);
        }
        return node;
    }

    private static IWorkflowSlotViewModel Slot() => new SlotDefaultViewModel();

    private static IWorkflowLinkViewModel Link(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        => new LinkDefaultViewModel { Sender = sender, Receiver = receiver, IsVisible = true };

    [TestMethod]
    public void NotVisible_NotReady()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = new LinkDefaultViewModel { Sender = a.Slots[0], Receiver = b.Slots[0] };
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsFalse(link.IsVisible);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void DefaultAnchor_NotReady()
    {
        // Default anchor is NaN (unmeasured) → not rendered, avoiding a mispositioned frame read from "no-value" coordinates.
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        Assert.IsTrue(double.IsNaN(a.Slots[0].Anchor.Horizontal));
        Assert.IsTrue(double.IsNaN(a.Slots[0].Anchor.Vertical));
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void PlaceholderEndpoint_Ready()
    {
        // A placeholder endpoint not attached to a node (empty Parent, e.g. drag-preview/virtual-link placeholders)
        // has no layout to wait on → ready.
        var link = Link(Slot(), Slot());
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void SingleEndpointMeasured_StillNotReady()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void BothEndpointsMeasured_Ready()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void MeasuredThenDefaulted_NotReady()
    {
        // Anchor returns from a measured value to the default NaN (e.g. a virtual-link reset) → not ready.
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());

        b.Slots[0].Anchor = new Anchor(double.NaN, double.NaN, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void OriginAnchor_IsMeasured()
    {
        // Real measurement can land exactly on the origin (0,0,0) (a node at the canvas origin) — this must count as ready.
        // The NaN check is unrelated to (0,0) — a pure value check (testing for 0) cannot express this.
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(0, 0, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void HorizontalNaNOnly_NotReady()
    {
        // NaN in only the horizontal coordinate is still not ready (any unmeasured component blocks rendering).
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(double.NaN, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void VerticalNaNOnly_NotReady()
    {
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, double.NaN, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void MeasurementResumed_Ready()
    {
        // Leaving (resetting to NaN) then measuring again → ready again.
        var a = NodeWithSlots(Slot());
        var b = NodeWithSlots(Slot());
        var link = Link(a.Slots[0], b.Slots[0]);
        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        Assert.IsTrue(link.IsRenderReady());

        a.Slots[0].Anchor = new Anchor(double.NaN, double.NaN, 0);
        Assert.IsFalse(link.IsRenderReady());

        a.Slots[0].Anchor = new Anchor(100, 50, 0);
        Assert.IsTrue(link.IsRenderReady());
    }

    [TestMethod]
    public void NodeWithoutSlots_LinkNotReady()
    {
        // A link endpoint references a slot that does not exist on its node → no measurement source, rendering is blocked.
        var a = NodeWithSlots();
        var b = NodeWithSlots(Slot());
        var link = Link(new SlotDefaultViewModel { Parent = a }, b.Slots[0]);
        Assert.IsFalse(link.IsRenderReady());
    }

    [TestMethod]
    public void DynamicSlot_MeasuredAfterCreation_Ready()
    {
        // A slot dynamically added after the node was created becomes ready once measured, with no subscription dependency.
        var node = NodeWithSlots();
        var b = NodeWithSlots(Slot());
        var dynamicSlot = Slot();
        dynamicSlot.Parent = node;
        node.Slots.Add(dynamicSlot);
        b.Slots[0].Anchor = new Anchor(300, 50, 0);
        dynamicSlot.Anchor = new Anchor(100, 50, 0);
        var link = Link(dynamicSlot, b.Slots[0]);
        Assert.IsTrue(link.IsRenderReady());
    }
}
