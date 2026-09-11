using VeloxDev.AT.Drivers;
using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Answers the one question a Jalium driver stands on: does the demo's window answer UI Automation from outside the
/// process, and do the <c>over.*</c> tokens appear in that tree?
/// </summary>
/// <remarks>
/// Kept apart from <see cref="LocatorProbeSuite"/> because it exists to settle a dispute rather than to smoke-test a
/// driver already believed to work. Jalium ships its own <c>AutomationPeer</c> hierarchy and a <c>WM_GETOBJECT</c>
/// handler that hands an <c>IRawElementProviderSimple</c> to <c>UiaReturnRawElementProvider</c>, which is the shape a
/// framework answers UIA with — but the framework also paints through D3D12 into its own top-level window, so whether
/// that provider is reachable from another process was an open question, not an assumption. This probe is the evidence:
/// when it fails it reports exactly what it saw — which window was found, how many descendants it had, and which ids
/// appeared — so the failure itself decides whether a driver can be written for the tree at all.
/// </remarks>
[TestClass]
public class JaliumLocatorProbeSuite
{
    /// <summary>
    /// Every token the Jalium demo marks with <c>AutomationProperties.SetAutomationId</c>: the six scenario buttons and
    /// the two readouts. The human readout is checked too — it was not replaced by the machine payload.
    /// </summary>
    private static readonly string[] Tokens =
    [
        "over.btn.back",
        "over.btn.elastic",
        "over.btn.color",
        "over.btn.size",
        "over.btn.brush",
        "over.btn.reset",
        "over.readout",
        "over.state",
    ];

    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Launch the demo and read the window's automation tree from outside the process. The tree description is written
    /// to the log unconditionally, so a pass records what the surface looked like and not merely that it existed.
    /// </summary>
    [TestMethod]
    [TestCategory("AT.Jalium")]
    public void Jalium_ObservationSurface_IsReachableFromOutsideTheProcess()
    {
        DemoCatalog.RequireEnabled(JaliumDemoDriver.PlatformName);

        using var host = new DesktopProcessHost();
        host.Start(AtConfig.DemoExecutable(JaliumDemoDriver.RelativeExecutablePath));

        // 先如实描述一次树：探测失败时不该只剩一句"等了 30 秒"，而要能看出是窗口没找到、还是树是空的、还是 id 没上去。
        var description = AutomationLocator.DescribeTree(host.ProcessId, TimeSpan.FromSeconds(30));
        TestContext.WriteLine(description);

        using var locator = AutomationLocator.Attach(host.ProcessId, TimeSpan.FromSeconds(30));

        var missing = Tokens.Where(token => !locator.Exists(token)).ToArray();

        Assert.AreEqual(0, missing.Length,
            $"The Jalium demo's window is not reachable through UI Automation the way a driver needs it to be. "
            + $"Missing ids: {string.Join(", ", missing)}. The probe saw: {description}");

        // 定位到了还不够：载荷必须是可解析的 v=1，且读数定时器已经在跑 —— 那才是别的套件真正依赖的东西。
        var payload = StatePayload.Parse(locator.Find("over.state", TimeSpan.FromSeconds(5)).Text);
        TestContext.WriteLine($"payload: {payload.Raw}");

        Assert.AreEqual(1, payload.Version, "The payload does not report format version 1.");
    }
}
