using VeloxDev.AT.Drivers;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Proves the thing the Blazor conformance suite assumes: that the demo server comes up, that its page is reachable
/// through the browser, and that its readout is actually ticking.
/// </summary>
/// <remarks>
/// Kept separate from the conformance suite because it fails for completely different reasons. When this goes red the
/// problem is the launch path, the page, or the payload's spelling; when a conformance check goes red a sampler is wrong.
/// <para>
/// The sequence handshake carries more weight here than on any other platform: a click — or a read — that lands before
/// the interactive circuit is live is silently dropped, so two reads whose sequence differs are the only proof the page
/// is actually running rather than showing its prerendered markup.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("AT.Blazor")]
public class BlazorProbeSuite
{
    /// <summary>
    /// Drives the Blazor demo's page: the readouts present, the payload parseable, and its sequence number advancing
    /// on its own.
    /// </summary>
    [TestMethod]
    public void Blazor_ObservationSurface_IsReachableAndTicking()
    {
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
        using var driver = DemoCatalog.Create(BlazorDemoDriver.PlatformName);
        driver.Launch();

        // 给人看的读数也必须还在：它没有被机器载荷取代，缺了说明演示面被动过。
        Assert.IsTrue(driver.HasControl("over.readout"), "The human-readable readout is missing.");

        // 拆解保证与桌面端不同，如实断言而不是照抄：浏览器由 Playwright 上下文关闭、Kestrel 由进程树杀灭（见 BlazorHost），
        // 它本来就不该在一个 kill-on-close 作业对象里 —— 那会连用户自己的浏览器会话一起收走。
        Assert.IsFalse(driver.IsInKillOnCloseJob,
            "The Blazor host must report that it is not in a job object: its teardown is the browser context and the server's process tree.");

        var first = driver.Read();
        Assert.AreEqual(driver.PayloadVersion, first.Version,
            $"The payload reports format version {first.Version}, but this driver understands version {driver.PayloadVersion}.");

        var second = driver.WaitForTick(first.Sequence);

        Assert.IsTrue(second.Sequence > first.Sequence,
            $"The readout sequence did not advance past {first.Sequence}; the demo's readout timer is not running, "
            + "which also means the interactive circuit never came up.");
    }
}
