using System.Globalization;
using System.Reflection;
using VeloxDev.AT.Conformance;
using VeloxDev.AT.Drivers;
using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Drives every sampler an adapter ships inside a real running app and compares what it wrote against the closed form
/// its entry states.
/// </summary>
/// <remarks>
/// This is the layer the pure-data suite cannot reach: a sampler whose produced value is a framework object needs a
/// live runtime to construct its endpoints at all, so the only place it can be driven is a real application that
/// already has one. The app does the driving — it publishes one frame per sampler per eased time on <c>over.conf</c> —
/// and the expectation is written here, from the rule, so nothing in this project can be answered by the sampler.
/// </remarks>
[TestClass]
public class SamplerConformanceSuite
{
    /// <summary>
    /// How far a produced component may be from the expected one. Both sides run the same arithmetic, but as two
    /// separately compiled copies of it, so the last bit may differ; this is far below any real rule difference.
    /// </summary>
    private const double Tolerance = 1e-6;

    [TestMethod]
    [TestCategory("AT.WPF")]
    public void Wpf_EverySamplerMatchesItsClosedForm() => AssertConforms(WpfConformance.Platform);

    [TestMethod]
    [TestCategory("AT.WinForms")]
    public void WinForms_EverySamplerMatchesItsClosedForm() => AssertConforms(WinFormsConformance.Platform);

    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void Blazor_EverySamplerMatchesItsClosedForm() => AssertConforms(BlazorConformance.Platform);

    [TestMethod]
    [TestCategory("AT.MAUI")]
    public void Maui_EverySamplerMatchesItsClosedForm() => AssertConforms(MauiConformance.Platform);

    [TestMethod]
    [TestCategory("AT.WinUI")]
    public void WinUI_EverySamplerMatchesItsClosedForm() => AssertConforms(WinUiConformance.Platform);

    /// <summary>
    /// Every platform with a closed-form table must have a case that runs it.
    /// </summary>
    /// <remarks>
    /// This is not belt-and-braces: a case was once lost by an edit that used the case above it as the anchor and did
    /// not put it back, and nothing went red — the platform simply stopped being verified while the suite stayed green.
    /// The registry is the assertion; this checks that each entry in it is actually reachable.
    /// </remarks>
    [TestMethod]
    public void EveryConformancePlatform_HasARunningCase()
    {
        var covered = typeof(SamplerConformanceSuite)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes<TestCategoryAttribute>())
            .SelectMany(attribute => attribute.TestCategories)
            .Where(category => category.StartsWith("AT.", StringComparison.Ordinal))
            .Select(category => category[3..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = ConformanceCatalog.Platforms
            .Where(platform => !covered.Contains(platform))
            .OrderBy(platform => platform, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(Array.Empty<string>(), missing.ToArray(),
            $"这些平台有闭式解表，却没有任何一条用例在跑它：{string.Join(", ", missing)}");
    }

    [TestMethod]
    [TestCategory("AT.Blazor")]
    public void Blazor_SamplerBench_PaintsTheColourTheSamplerProduced()
    {
        // 这条是 Blazor 独有的形态，也是"从真实 UI 表现验证"最字面的一种：浏览器里没有一个"控件属性"可读，
        // 真实表现就是**计算样式**。所以这里不读 app 报的载荷，而是读浏览器算出来的背景色，与采样器端点色比。
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
        using var driver = DemoCatalog.Create(BlazorDemoDriver.PlatformName);
        driver.Launch();

        driver.ActivateSampler("StringSampler");

        // 等演出跑完（800ms），浏览器把最后一帧画上去 —— 那一帧对应 t=1，也就是端点色。
        Thread.Sleep(1200);

        var painted = driver.ComputedStyle("over.bench", "background-color");
        Assert.IsNotNull(painted, "浏览器应当能给出 over.bench 的计算样式");

        // 期望值取自 StringSampler 声明的端点色 #F0B43240（R240 G180 B50），不是取自载荷。
        var declared = RgbColor.Parse("#F0B432");
        var actual = CssColor.Parse(painted);

        Assert.AreEqual(declared, actual,
            $"浏览器算出的背景色是 {painted}，而采样器的端点色是 #F0B432");
    }

    [TestMethod]
    [TestCategory("AT.Jalium")]
    public void Jalium_EverySamplerMatchesItsClosedForm() => AssertConforms(JaliumConformance.Platform);

    [TestMethod]
    [TestCategory("AT.Avalonia")]
    public void Avalonia_EverySamplerMatchesItsClosedForm() => AssertConforms(AvaloniaConformance.Platform);

    private static void AssertConforms(string platform)
    {
        DemoCatalog.RequireEnabled(platform);
        using var driver = DemoCatalog.Create(platform);
        driver.Launch();

        var table = ConformanceCatalog.For(platform);

        // 收集全部不符再一次性报出：一个采样器错在哪一步，比"第一个失败就停"有用得多。
        var failures = new List<string>();

        // 逐个点击：表里每条采样器对应界面上一个把手，点它、等 seq 越过、读回。没有把手就点不到，
        // 覆盖守卫因此是隐式的 —— "表里有、demo 没实现"当场就红。
        foreach (var entry in table)
        {
            ConformancePayload payload;
            try
            {
                payload = driver.ActivateSampler(entry.Sampler);
            }
            catch (Exception exception)
            {
                failures.Add($"激活 {entry.Sampler} 失败：{exception.Message}");
                continue;
            }

            if (payload.Version != 1)
                failures.Add($"{entry.Sampler}：载荷版本是 {payload.Version}，这个套件只认 1。");

            if (payload.DeclaredCount != payload.Frames.Count)
                failures.Add($"{entry.Sampler}：demo 声明写了 {payload.DeclaredCount} 帧，实际解析出 {payload.Frames.Count} 帧。");

            // 点的是谁，报回来的就该是谁 —— 载荷里混进另一条采样器的帧，说明把手与探针表不同步。
            var reported = payload.Frames.Select(frame => frame.Sampler).Distinct().ToList();
            if (reported.Count != 1 || reported[0] != entry.Sampler)
            {
                failures.Add($"{entry.Sampler}：点它却报回了 [{string.Join(", ", reported)}]。");
                continue;
            }

            for (var index = 0; index < ConformanceCatalog.Times.Length; index++)
            {
                var t = ConformanceCatalog.Times[index];
                var frame = payload.Frames.FirstOrDefault(f => f.TimeIndex == index);

                if (frame is null)
                {
                    failures.Add($"{entry.Sampler} 在 t={Format(t)} 上没有帧。");
                    continue;
                }

                if (frame.TypeTag != entry.TypeTag)
                {
                    failures.Add($"{entry.Sampler} t={Format(t)}：产物类型是 {frame.TypeTag}，期望 {entry.TypeTag}。");
                    continue;
                }

                var expected = entry.Expected(t);
                if (!Matches(expected, frame.Components))
                {
                    failures.Add(
                        $"{entry.Sampler} t={Format(t)}：期望 [{Format(expected)}]，实际 [{Format(frame.Components)}]。");
                }
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), failures.ToArray(),
            $"{platform} 有 {failures.Count} 处与闭式解不符：{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>
    /// Compares component vectors. A length difference is a failure, never a comparison of the common prefix: a
    /// sampler that started producing a different shape has to be reported, not partially matched.
    /// </summary>
    private static bool Matches(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        if (expected.Count != actual.Count) return false;

        for (var i = 0; i < expected.Count; i++)
        {
            if (Math.Abs(expected[i] - actual[i]) > Tolerance) return false;
        }

        return true;
    }

    private static string Format(double value) => value.ToString("G6", CultureInfo.InvariantCulture);

    private static string Format(IReadOnlyList<double> values)
        => string.Join(", ", values.Select(Format));
}
