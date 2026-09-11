using VeloxDev.AT.Drivers;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Proves the thing the Blazor overshoot suite assumes: that the demo server comes up, that its page is reachable
/// through the browser, and that its readout is actually ticking.
/// </summary>
/// <remarks>
/// Kept separate from the overshoot suite because it fails for completely different reasons. When this goes red the
/// problem is the launch path, the page, or the payload's spelling; when the overshoot suite goes red the animation is
/// wrong. Sorting a failure into one of those two buckets first is most of the diagnosis.
/// <para>
/// It lives in its own file rather than in <c>LocatorProbeSuite</c> so that adding Blazor touches no file another
/// platform's author owns; the category filter <c>AT.Blazor</c> selects it just the same.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("AT.Blazor")]
public class BlazorProbeSuite
{
    /// <summary>
    /// Drives the Blazor demo's page: every scenario button present, both readouts present, the payload parseable, and
    /// its sequence number advancing on its own.
    /// </summary>
    [TestMethod]
    public void Blazor_ObservationSurface_IsReachableAndTicking()
    {
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
        using var driver = DemoCatalog.Create(BlazorDemoDriver.PlatformName);
        driver.Launch();

        // 每个场景按钮都必须能被定位到：套件点的是 data-at 令牌，不是中文文案。
        foreach (var scenario in driver.Scenarios)
        {
            Assert.IsTrue(driver.HasControl(scenario.ButtonAutomationId),
                $"The control '{scenario.ButtonAutomationId}' is missing, so {scenario} cannot be run.");
        }

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
        Assert.AreEqual("none", first.Scenario, "A freshly launched demo should be idle.");
        Assert.AreEqual(first.Scenario, second.Scenario, "Merely reading the payload must not start a scenario.");
    }
}
