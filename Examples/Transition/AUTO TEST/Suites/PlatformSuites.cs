using VeloxDev.AT.Drivers;

namespace VeloxDev.AT.Suites;

// -----------------------------------------------------------------------------------------------------------------
// 一个平台一个类，而不是一个方面一个类。
//
// 这决定的是**执行顺序**，而顺序和使用体验是一回事。按方面分（"加载模式套件"跑七个平台，"采样器套件"再跑七个）
// 会让一次运行变成：七个窗口依次开出来、全部留在桌面上，然后在它们之间来回跳四轮 —— 人看到的是七个 demo 反复
// 被唤到前台，而不是"一个 demo 被完整测一遍"。
//
// 按平台分之后，MSTest 一个类一个类地跑完（程序集已 DoNotParallelize），于是每个平台是：开一个窗口 → 把这个平台
// 的四个方面依次驱动完 → 关掉 → 再开下一个。全程桌面上只有一个窗口，并且它在离开前已经被走完了。
//
// 每个类自己开自己关（ClassInitialize/ClassCleanup），因为 MSTest 只有夹具能做这件事；方面本身的实现都在
// Suites/*Checks.cs 里，由这些壳调用。加一个新平台 = 加一个这样的类 + 在 DemoCatalog 登记一行。
// -----------------------------------------------------------------------------------------------------------------

/// <summary>
/// WPF 的验收：一个窗口，四个方面。
/// </summary>
[TestClass]
public class WpfAcceptanceSuite
{
    private static IDemoDriver? _demo;

    [ClassInitialize]
    public static void OpenDemo(TestContext context) => _demo = DemoCatalog.TryOpen(WpfDemoDriver.PlatformName);

    [ClassCleanup]
    public static void CloseDemo() => DemoCatalog.Release(WpfDemoDriver.PlatformName);

    /// <summary>这个类的 demo。禁用时由 RequireEnabled 抛出跳过，所以夹具那一层不需要断言任何东西。</summary>
    private static IDemoDriver Demo()
    {
        DemoCatalog.RequireEnabled(WpfDemoDriver.PlatformName);
        return _demo ?? DemoCatalog.For(WpfDemoDriver.PlatformName);
    }

    [TestMethod]
    [TestCategory("AT.WPF")]
    public void ObservationSurface_IsReachableAndTicking() => ProbeChecks.Run(Demo(), expectKillOnCloseJob: true);

    [TestMethod]
    [TestCategory("AT.WPF")]
    public void LoadModes_MatchTheLibrarySemantics() => LoadModeChecks.Run(Demo(), WpfDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.WPF")]
    public void EverySamplerMatchesItsClosedForm() => ConformanceChecks.Run(Demo(), WpfDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.WPF")]
    public void TimelineControl_SteersTheRunningAnimation() => TimelineControlChecks.Run(Demo(), WpfDemoDriver.PlatformName);
}

/// <summary>
/// Avalonia 的验收：一个窗口，四个方面。
/// </summary>
[TestClass]
public class AvaloniaAcceptanceSuite
{
    private static IDemoDriver? _demo;

    [ClassInitialize]
    public static void OpenDemo(TestContext context) => _demo = DemoCatalog.TryOpen(AvaloniaDemoDriver.PlatformName);

    [ClassCleanup]
    public static void CloseDemo() => DemoCatalog.Release(AvaloniaDemoDriver.PlatformName);

    private static IDemoDriver Demo()
    {
        DemoCatalog.RequireEnabled(AvaloniaDemoDriver.PlatformName);
        return _demo ?? DemoCatalog.For(AvaloniaDemoDriver.PlatformName);
    }

    [TestMethod]
    [TestCategory("AT.Avalonia")]
    public void ObservationSurface_IsReachableAndTicking() => ProbeChecks.Run(Demo(), expectKillOnCloseJob: true);

    [TestMethod]
    [TestCategory("AT.Avalonia")]
    public void LoadModes_MatchTheLibrarySemantics() => LoadModeChecks.Run(Demo(), AvaloniaDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.Avalonia")]
    public void EverySamplerMatchesItsClosedForm() => ConformanceChecks.Run(Demo(), AvaloniaDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.Avalonia")]
    public void TimelineControl_SteersTheRunningAnimation() => TimelineControlChecks.Run(Demo(), AvaloniaDemoDriver.PlatformName);
}

/// <summary>
/// WinUI 的验收：一个窗口，四个方面。
/// </summary>
[TestClass]
public class WinUiAcceptanceSuite
{
    private static IDemoDriver? _demo;

    [ClassInitialize]
    public static void OpenDemo(TestContext context) => _demo = DemoCatalog.TryOpen(WinUIDemoDriver.PlatformName);

    [ClassCleanup]
    public static void CloseDemo() => DemoCatalog.Release(WinUIDemoDriver.PlatformName);

    private static IDemoDriver Demo()
    {
        DemoCatalog.RequireEnabled(WinUIDemoDriver.PlatformName);
        return _demo ?? DemoCatalog.For(WinUIDemoDriver.PlatformName);
    }

    [TestMethod]
    [TestCategory("AT.WinUI")]
    public void ObservationSurface_IsReachableAndTicking() => ProbeChecks.Run(Demo(), expectKillOnCloseJob: true);

    [TestMethod]
    [TestCategory("AT.WinUI")]
    public void LoadModes_MatchTheLibrarySemantics() => LoadModeChecks.Run(Demo(), WinUIDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.WinUI")]
    public void EverySamplerMatchesItsClosedForm() => ConformanceChecks.Run(Demo(), WinUIDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.WinUI")]
    public void TimelineControl_SteersTheRunningAnimation() => TimelineControlChecks.Run(Demo(), WinUIDemoDriver.PlatformName);
}

/// <summary>
/// MAUI 的验收：一个窗口，四个方面。
/// </summary>
[TestClass]
public class MauiAcceptanceSuite
{
    private static IDemoDriver? _demo;

    [ClassInitialize]
    public static void OpenDemo(TestContext context) => _demo = DemoCatalog.TryOpen(MauiDemoDriver.PlatformName);

    [ClassCleanup]
    public static void CloseDemo() => DemoCatalog.Release(MauiDemoDriver.PlatformName);

    private static IDemoDriver Demo()
    {
        DemoCatalog.RequireEnabled(MauiDemoDriver.PlatformName);
        return _demo ?? DemoCatalog.For(MauiDemoDriver.PlatformName);
    }

    [TestMethod]
    [TestCategory("AT.MAUI")]
    public void ObservationSurface_IsReachableAndTicking() => ProbeChecks.Run(Demo(), expectKillOnCloseJob: true);

    [TestMethod]
    [TestCategory("AT.MAUI")]
    public void LoadModes_MatchTheLibrarySemantics() => LoadModeChecks.Run(Demo(), MauiDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.MAUI")]
    public void EverySamplerMatchesItsClosedForm() => ConformanceChecks.Run(Demo(), MauiDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.MAUI")]
    public void TimelineControl_SteersTheRunningAnimation() => TimelineControlChecks.Run(Demo(), MauiDemoDriver.PlatformName);
}

/// <summary>
/// WinForms 的验收：一个窗口，四个方面。
/// </summary>
[TestClass]
public class WinFormsAcceptanceSuite
{
    private static IDemoDriver? _demo;

    [ClassInitialize]
    public static void OpenDemo(TestContext context) => _demo = DemoCatalog.TryOpen(WinFormsDemoDriver.PlatformName);

    [ClassCleanup]
    public static void CloseDemo() => DemoCatalog.Release(WinFormsDemoDriver.PlatformName);

    private static IDemoDriver Demo()
    {
        DemoCatalog.RequireEnabled(WinFormsDemoDriver.PlatformName);
        return _demo ?? DemoCatalog.For(WinFormsDemoDriver.PlatformName);
    }

    [TestMethod]
    [TestCategory("AT.WinForms")]
    public void ObservationSurface_IsReachableAndTicking() => ProbeChecks.Run(Demo(), expectKillOnCloseJob: true);

    [TestMethod]
    [TestCategory("AT.WinForms")]
    public void LoadModes_MatchTheLibrarySemantics() => LoadModeChecks.Run(Demo(), WinFormsDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.WinForms")]
    public void EverySamplerMatchesItsClosedForm() => ConformanceChecks.Run(Demo(), WinFormsDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.WinForms")]
    public void TimelineControl_SteersTheRunningAnimation() => TimelineControlChecks.Run(Demo(), WinFormsDemoDriver.PlatformName);
}

/// <summary>
/// Jalium 的验收：一个窗口，四个方面。
/// </summary>
[TestClass]
public class JaliumAcceptanceSuite
{
    private static IDemoDriver? _demo;

    [ClassInitialize]
    public static void OpenDemo(TestContext context) => _demo = DemoCatalog.TryOpen(JaliumDemoDriver.PlatformName);

    [ClassCleanup]
    public static void CloseDemo() => DemoCatalog.Release(JaliumDemoDriver.PlatformName);

    private static IDemoDriver Demo()
    {
        DemoCatalog.RequireEnabled(JaliumDemoDriver.PlatformName);
        return _demo ?? DemoCatalog.For(JaliumDemoDriver.PlatformName);
    }

    [TestMethod]
    [TestCategory("AT.Jalium")]
    public void ObservationSurface_IsReachableAndTicking() => ProbeChecks.Run(Demo(), expectKillOnCloseJob: true);

    [TestMethod]
    [TestCategory("AT.Jalium")]
    public void LoadModes_MatchTheLibrarySemantics() => LoadModeChecks.Run(Demo(), JaliumDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.Jalium")]
    public void EverySamplerMatchesItsClosedForm() => ConformanceChecks.Run(Demo(), JaliumDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.Jalium")]
    public void TimelineControl_SteersTheRunningAnimation() => TimelineControlChecks.Run(Demo(), JaliumDemoDriver.PlatformName);
}

/// <summary>
/// Blazor 的验收：一个页面，四个方面，外加一条只有浏览器才有的检查。
/// </summary>
[TestClass]
public class BlazorAcceptanceSuite
{
    private static IDemoDriver? _demo;

    [ClassInitialize]
    public static void OpenDemo(TestContext context) => _demo = DemoCatalog.TryOpen(BlazorDemoDriver.PlatformName);

    [ClassCleanup]
    public static void CloseDemo() => DemoCatalog.Release(BlazorDemoDriver.PlatformName);

    private static IDemoDriver Demo()
    {
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
        return _demo ?? DemoCatalog.For(BlazorDemoDriver.PlatformName);
    }

    /// <summary>浏览器宿主不在 kill-on-close 作业对象里，理由见 <see cref="ProbeChecks.Run"/>。</summary>
    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void ObservationSurface_IsReachableAndTicking() => ProbeChecks.Run(Demo(), expectKillOnCloseJob: false);

    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void LoadModes_MatchTheLibrarySemantics() => LoadModeChecks.Run(Demo(), BlazorDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void EverySamplerMatchesItsClosedForm() => ConformanceChecks.Run(Demo(), BlazorDemoDriver.PlatformName);

    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void TimelineControl_SteersTheRunningAnimation() => TimelineControlChecks.Run(Demo(), BlazorDemoDriver.PlatformName);

    /// <summary>只有浏览器能这么验：读浏览器算出来的背景色，而不是 app 自己报的载荷。</summary>
    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void SamplerBench_PaintsTheColourTheSamplerProduced() => ConformanceChecks.RunBlazorBench(Demo());
}
