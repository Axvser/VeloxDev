using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Verifies the Debug-only attachment guards (<see cref="WorkflowGuard"/>). A command
/// invoked on a component that is not mounted (slot/node/link detached, selector not
/// installed) must throw InvalidOperationException in DEBUG builds. The asserts are
/// wrapped in #if DEBUG because [Conditional("DEBUG")] removes the guard call sites in
/// Release builds, where the same operations silently no-op by design.
/// </summary>
[TestClass]
public class WorkflowGuardTests
{
    [TestMethod]
    public void SetChannelOnDetachedSlot_ThrowsInDebug()
    {
#if DEBUG
        var slot = new SlotDefaultViewModel();
        try { slot.GetHelper().SetChannel(SlotChannel.OneTarget); Assert.Fail(); }
        catch (InvalidOperationException) { }
#endif
    }

    [TestMethod]
    public void SendConnectionOnDetachedSlot_ThrowsInDebug()
    {
#if DEBUG
        var slot = new SlotDefaultViewModel();
        try { slot.GetHelper().SendConnection(); Assert.Fail(); }
        catch (InvalidOperationException) { }
#endif
    }

    [TestMethod]
    public void ReceiveConnectionOnDetachedSlot_ThrowsInDebug()
    {
#if DEBUG
        var slot = new SlotDefaultViewModel();
        try { slot.GetHelper().ReceiveConnection(); Assert.Fail(); }
        catch (InvalidOperationException) { }
#endif
    }

    [TestMethod]
    public void DeleteDetachedSlot_ThrowsInDebug()
    {
#if DEBUG
        var slot = new SlotDefaultViewModel();
        try { slot.GetHelper().Delete(); Assert.Fail(); }
        catch (InvalidOperationException) { }
#endif
    }

    [TestMethod]
    public void DeleteDetachedNode_ThrowsInDebug()
    {
#if DEBUG
        var node = new NodeDefaultViewModel();
        try { node.GetHelper().Delete(); Assert.Fail(); }
        catch (InvalidOperationException) { }
#endif
    }

    [TestMethod]
    public void DeleteDetachedLink_ThrowsInDebug()
    {
#if DEBUG
        var link = new LinkDefaultViewModel();
        try { link.GetHelper().Delete(); Assert.Fail(); }
        catch (InvalidOperationException) { }
#endif
    }

    [TestMethod]
    public void SetSelectorOnUninstalledEnumerator_ThrowsInDebug()
    {
#if DEBUG
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        try { enumerator.SetSelector(typeof(bool)); Assert.Fail(); }
        catch (InvalidOperationException) { }
#endif
    }

    /// <summary>
    /// Release counterpart: [Conditional("DEBUG")] removes every WorkflowGuard.Fail call
    /// site, so the same detached operations must NOT throw and must leave no side effects
    /// (the historical silent no-op). Only meaningful when run under a Release build.
    /// </summary>
    [TestMethod]
    public void DetachedOperations_SilentNoOpInRelease()
    {
#if !DEBUG
        var slot = new SlotDefaultViewModel();
        slot.GetHelper().SetChannel(SlotChannel.OneTarget); // no throw
        Assert.AreEqual(SlotChannel.OneBoth, slot.Channel, "SetChannel on a detached slot is a silent no-op in Release.");

        var node = new NodeDefaultViewModel();
        node.GetHelper().Delete(); // no throw

        var link = new LinkDefaultViewModel();
        link.GetHelper().Delete(); // no throw

        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.SetSelector(typeof(bool)); // no throw
#endif
    }
}
