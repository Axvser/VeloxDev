using VeloxDev.AT.Drivers;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Proves the thing every other suite assumes: that a demo starts, that its observation surface is reachable through
/// UI Automation, and that its readout is actually ticking.
/// </summary>
/// <remarks>
/// Kept separate from the conformance suite because it fails for completely different reasons. When this goes red the
/// problem is the launch path, the automation tree, or the payload's spelling; when a conformance check goes red a
/// sampler is wrong. Sorting a failure into one of those two buckets first is most of the diagnosis.
/// </remarks>
[TestClass]
public class WpfProbeSuite
{
    /// <summary>
    /// Drives the WPF demo's surface: the readouts present, the payload parseable, and its sequence number advancing
    /// on its own.
    /// </summary>
    [TestMethod]
    [TestCategory("AT.WPF")]
    public void Wpf_ObservationSurface_IsReachableAndTicking()
    {
        DemoCatalog.RequireEnabled(WpfDemoDriver.PlatformName);
        using var driver = DemoCatalog.Create(WpfDemoDriver.PlatformName);
        driver.Launch();

        // 给人看的读数必须还在：它没有被机器载荷取代，缺了说明演示面被动过。
        Assert.IsTrue(driver.HasControl("over.readout"), "The human-readable readout is missing.");

        // 拆解保证也要验，而不是假定：宿主被直接杀掉时，作业对象是唯一还会回收 demo 的东西。
        Assert.IsTrue(driver.IsInKillOnCloseJob,
            "The demo is not inside the kill-on-close job object, so a test host that dies without cleanup would leave it running.");

        var first = driver.Read();
        Assert.AreEqual(driver.PayloadVersion, first.Version,
            $"The payload reports format version {first.Version}, but this driver understands version {driver.PayloadVersion}.");

        var second = driver.WaitForTick(first.Sequence);

        Assert.IsTrue(second.Sequence > first.Sequence,
            $"The readout sequence did not advance past {first.Sequence}; the demo's readout timer is not running.");
    }
}
