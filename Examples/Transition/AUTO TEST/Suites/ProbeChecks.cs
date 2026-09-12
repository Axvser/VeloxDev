using VeloxDev.AT.Drivers;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Proves the thing every other check assumes: that a demo starts, that its observation surface is reachable, and
/// that its payload is actually ticking.
/// </summary>
/// <remarks>
/// Kept separate from the conformance checks because it fails for completely different reasons. When this goes red the
/// problem is the launch path, the automation tree, or the payload's spelling; when a conformance check goes red a
/// sampler is wrong. Sorting a failure into one of those two buckets first is most of the diagnosis.
/// <para>
/// The demos no longer carry a human-readable readout beside the payloads — it only restated them in prose and sat
/// between the toolbar and the case list. Nothing here depended on it beyond its existence; the payload's advancing
/// sequence number is what proves the demo's timer is alive, and that is asserted below.
/// </para>
/// </remarks>
internal static class ProbeChecks
{
    /// <summary>
    /// Drives the demo's observation surface, through a demo the caller already owns.
    /// </summary>
    /// <param name="expectKillOnCloseJob">
    /// Whether this host is expected to sit inside the kill-on-close job object. True for a desktop window, whose
    /// process the job is the last resort for; false for the browser one, whose teardown is the Playwright context and
    /// the server's process tree — putting it in the job would take the user's own browser session down with it.
    /// </param>
    internal static void Run(IDemoDriver driver, bool expectKillOnCloseJob)
    {
        driver.Settle();

        // 拆解保证按平台不同，如实断言而不是照抄。
        if (expectKillOnCloseJob)
        {
            Assert.IsTrue(driver.IsInKillOnCloseJob,
                "The demo is not inside the kill-on-close job object, so a test host that dies without cleanup would leave it running.");
        }
        else
        {
            Assert.IsFalse(driver.IsInKillOnCloseJob,
                "This host must report that it is not in a job object: its teardown is the browser context and the server's process tree.");
        }

        var first = driver.Read();
        Assert.AreEqual(driver.PayloadVersion, first.Version,
            $"The payload reports format version {first.Version}, but this driver understands version {driver.PayloadVersion}.");

        var second = driver.WaitForTick(first.Sequence);

        // 浏览器上这一条比其他平台承重得多：交互电路还没起来时，点击和读取都会被静默丢弃，所以"两次读到的序号不同"
        // 是页面真的在跑、而不是只显示了预渲染标记的唯一证据。
        Assert.IsTrue(second.Sequence > first.Sequence,
            $"The readout sequence did not advance past {first.Sequence}; the demo's readout timer is not running"
            + (expectKillOnCloseJob ? "." : ", which also means the interactive circuit never came up."));
    }
}
