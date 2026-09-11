using System.Globalization;
using System.Reflection;
using VeloxDev.AT.Drivers;
using VeloxDev.AT.Engine;
using VeloxDev.AT.LoadMode;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Drives the demo's load-mode row — three shapes and the five ways they can be loaded — and checks what the
/// <c>Transition&lt;T&gt;</c> pipeline actually did to them.
/// </summary>
/// <remarks>
/// This is the half the sampler suite cannot reach: that one verifies an <c>ISampler</c>'s arithmetic, this one
/// verifies the machinery around it — that a load starts, that a background-thread load starts too, that concurrent
/// and exclusive loads are actually different, that stop freezes rather than jumps, and that reset restores exactly
/// and stays restored.
/// <para>
/// <b>Everything here is model-free.</b> The library offers no completion signal for a run — <c>Execute</c> is
/// <c>async void</c>, the effect's events fire on an internal clone rather than on the builder's effect, and the
/// mutual scheduler is cached per target for its lifetime rather than being a liveness flag. So rather than
/// re-deriving where each of these animations <em>should</em> end (they auto-reverse and loop, and every platform's
/// are different), each case asserts a property that holds whatever the animation is: it moves when loaded, it stops
/// when stopped, and it returns exactly to its declared rest state when reset.
/// </para>
/// <para>
/// One case per platform rather than one per behaviour, because the expensive part is launching the demo and each
/// case here is a few hundred milliseconds of driving once it is up; failures are collected and reported together,
/// so a single run still names every behaviour that went wrong.
/// </para>
/// </remarks>
[TestClass]
public class LoadModeSuite
{
    /// <summary>How long a load may take to show up in the three targets.</summary>
    private static readonly TimeSpan StartWindow = TimeSpan.FromSeconds(3);

    /// <summary>How long apart two samples are taken when checking that something has stopped moving.</summary>
    private static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(700);

    private const double Tolerance = 1e-6;

    [TestMethod]
    [TestCategory("AT.WPF")]
    public void Wpf_LoadModes_MatchTheLibrarySemantics() => AssertLoadModes(WpfLoadMode.Platform);

    [TestMethod]
    [TestCategory("AT.Jalium")]
    public void Jalium_LoadModes_MatchTheLibrarySemantics() => AssertLoadModes(JaliumLoadMode.Platform);

    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void Blazor_LoadModes_MatchTheLibrarySemantics() => AssertLoadModes(BlazorLoadMode.Platform);

    [TestMethod]
    [TestCategory("AT.MAUI")]
    public void Maui_LoadModes_MatchTheLibrarySemantics() => AssertLoadModes(MauiLoadMode.Platform);

    [TestMethod]
    [TestCategory("AT.WinForms")]
    public void WinForms_LoadModes_MatchTheLibrarySemantics() => AssertLoadModes(WinFormsLoadMode.Platform);

    [TestMethod]
    [TestCategory("AT.WinUI")]
    public void WinUI_LoadModes_MatchTheLibrarySemantics() => AssertLoadModes(WinUiLoadMode.Platform);

    [TestMethod]
    [TestCategory("AT.Avalonia")]
    public void Avalonia_LoadModes_MatchTheLibrarySemantics() => AssertLoadModes(AvaloniaLoadMode.Platform);

    /// <summary>
    /// Every platform with a load-mode table must have a case that runs it — see the same guard in
    /// <c>SamplerConformanceSuite</c> for why this is not belt-and-braces.
    /// </summary>
    [TestMethod]
    public void EveryLoadModePlatform_HasARunningCase()
    {
        var covered = typeof(LoadModeSuite)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes<TestCategoryAttribute>())
            .SelectMany(attribute => attribute.TestCategories)
            .Where(category => category.StartsWith("AT.", StringComparison.Ordinal))
            .Select(category => category[3..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = LoadModeCatalog.Platforms
            .Where(platform => !covered.Contains(platform))
            .OrderBy(platform => platform, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), missing.ToArray(),
            $"这些平台有加载模式表，却没有任何一条用例在跑它：{string.Join(", ", missing)}");
    }

    private static void AssertLoadModes(string platform)
    {
        DemoCatalog.RequireEnabled(platform);
        using var driver = DemoCatalog.Create(platform);
        driver.Launch();

        var entry = LoadModeCatalog.For(platform);
        var failures = new List<string>();

        // 先确认这一排把手都在**人够得着**的地方。UI Automation 的 Invoke 对"从没被布局进窗口"的控件照样生效，
        // 少了这一道，一排没人看得见的按钮会让下面每条用例都绿着过去 —— WinUI 上真实发生过：第二排按钮
        // 一直被排在窗口外（UIA 报 IsOffscreen，截图里根本没有），而套件照点不误。
        foreach (var token in new[]
                 {
                     "over.btn.load.main", "over.btn.load.background",
                     "over.btn.load.main.concurrent", "over.btn.load.background.concurrent",
                     "over.btn.load.repeat", "over.btn.reset.all", "over.btn.stop.all",
                 })
        {
            if (!driver.IsControlInView(token))
            {
                failures.Add($"把手 {token} 不在窗口内，人够不着 —— 下面每条用例都可能是在点一个看不见的按钮");
            }
        }

        if (failures.Count > 0)
        {
            CollectionAssert.AreEqual(Array.Empty<string>(), failures.ToArray(),
                $"{platform} 的加载模式把手不全在窗口内：{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
        }

        void Case(string name, Action body)
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failures.Add($"{name}：{exception.Message}");
            }
        }

        // 每个用例都从静止态起步：上一个用例可能留下了还在飞的动画。
        void Reset()
        {
            driver.Click("over.btn.reset.all");
            driver.WaitForTick();
            driver.WaitFor(payload => AtRest(payload, entry), StartWindow, "重置后回到声明的静止态");
        }

        /// <summary>点一个加载按钮，断言三块目标的状态偏离静止态。</summary>
        void AssertLoadStarts(string button, string what)
        {
            Reset();
            var before = Observe(driver.Read(), entry);

            driver.Click(button);
            var after = Observe(
                driver.WaitFor(payload => Observe(payload, entry) != before, StartWindow, $"{what}后目标仍未变化"),
                entry);

            Assert.AreNotEqual(before, after, $"{what}之后目标状态必须改变");
        }

        Case("主线程加载让目标动起来", () => AssertLoadStarts("over.btn.load.main", "主线程加载"));

        // 这条不是上一条的复制：库里 Prepare 要阻塞地 Dispatcher.Invoke 回读初值，后台线程调 Execute 是有
        // 死锁面的，而 demo 的八个处理器里有一半走这条路。
        Case("后台线程加载让目标动起来", () => AssertLoadStarts("over.btn.load.background", "后台线程加载"));

        Case("并发加载登记并发运行，互斥加载不登记", () =>
        {
            Reset();

            driver.Click("over.btn.load.main.concurrent");
            var running = driver.WaitFor(
                payload => payload.Number("nomutual") >= 1, StartWindow, "并发加载后 nomutual 仍为 0");

            Assert.IsTrue(running.Number("nomutual") >= 1,
                $"并发加载后应至少有一条并发运行在册，实际 {running.Number("nomutual")}");

            driver.Click("over.btn.stop.all");
            driver.WaitFor(payload => payload.Number("nomutual") == 0, StartWindow, "停止全部后并发运行未清零");

            driver.Click("over.btn.load.main");
            driver.WaitForTick();

            Assert.AreEqual(0d, driver.Read().Number("nomutual"), Tolerance,
                "互斥加载不该登记成并发运行 —— 否则 nomutual 就区分不出这两条路径了");
        });

        Case("停止全部把目标冻在原地", () =>
        {
            Reset();
            var before = Observe(driver.Read(), entry);

            driver.Click("over.btn.load.main");
            driver.WaitFor(payload => Observe(payload, entry) != before, StartWindow,
                "加载后目标没动，那么“停止后不动”是空转");

            driver.Click("over.btn.stop.all");
            driver.WaitForTick();

            var frozen = Observe(driver.Read(), entry);
            Thread.Sleep(QuietWindow);

            Assert.AreEqual(frozen, Observe(driver.Read(), entry),
                "停止全部之后目标必须不再变化（Exit 是原地冻结，不是跳到终点）");
        });

        Case("重置精确回到声明的初始态并保持", () =>
        {
            Reset();

            // 飞行中重置：这才撞得上“取消前已排队的帧落地时必须丢弃”那条保证。
            driver.Click("over.btn.load.main");
            Thread.Sleep(400);
            driver.Click("over.btn.reset.all");

            var restored = driver.WaitFor(
                payload => AtRest(payload, entry), StartWindow, "重置后没有回到声明的初始态");

            AssertAtRest(restored, entry, "重置");

            Thread.Sleep(QuietWindow);
            AssertAtRest(driver.Read(), entry, "重置后必须保持住");
        });

        CollectionAssert.AreEqual(Array.Empty<string>(), failures.ToArray(),
            $"{platform} 的加载模式有 {failures.Count} 处不符：{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>三块目标的观测状态，按表里声明的字段顺序拼起来 —— 比"整串相等/不等"就够了。</summary>
    private static string Observe(StatePayload payload, LoadModeEntry entry)
        => string.Join('|', entry.Initial.Keys.Select(payload.Text));

    private static bool AtRest(StatePayload payload, LoadModeEntry entry)
    {
        foreach (var (key, expected) in entry.Initial)
        {
            var actual = payload.Text(key);
            if (!double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var wanted))
            {
                if (expected != actual) return false;
                continue;
            }

            if (Math.Abs(wanted - payload.Number(key)) > Tolerance) return false;
        }

        return true;
    }

    private static void AssertAtRest(StatePayload payload, LoadModeEntry entry, string because)
    {
        foreach (var (key, expected) in entry.Initial)
        {
            var actual = payload.Text(key);
            if (!double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var wanted))
            {
                Assert.AreEqual(expected, actual, $"{because}：{key}");
                continue;
            }

            Assert.AreEqual(wanted, payload.Number(key), Tolerance, $"{because}：{key} 期望 {expected}，实际 {actual}");
        }
    }
}
