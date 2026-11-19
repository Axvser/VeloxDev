using VeloxDev.AT.Conformance;
using VeloxDev.AT.Drivers;

namespace VeloxDev.AT.Suites;

/// <summary>
/// Repeats the sequence a reachability failure happens in, at a rate a real run cannot reach.
/// </summary>
/// <remarks>
/// Built while chasing a WinUI intermittent in which a sampler row reports an empty rectangle after being brought into
/// view. A full seven-platform run produces that sequence a few times per minute; this produces it about fifty times in
/// ten seconds, which is what makes a one-in-fifteen event investigable at all.
/// <para>
/// Ignored by default — it is a tool, not a check, and it drives the demo far harder than the suite does. Remove the
/// <see cref="IgnoreAttribute"/> when chasing a reachability failure: it prints what it saw and writes a screenshot to
/// the temp directory for every occurrence.
/// </para>
/// </remarks>
[TestClass]
public class ReachabilityStress
{
    [TestMethod]
    [Ignore("诊断工具，不是检查项。排查可达性失败时去掉这个标记。")]
    
    public void Stress_ReactivatingARowWhileTheDemoIsBusy()
    {
        DemoCatalog.RequireEnabled(WinUIDemoDriver.PlatformName);
        var driver = DemoCatalog.For(WinUIDemoDriver.PlatformName);
        driver.Settle();

        var rows = ConformanceCatalog.For(WinUIDemoDriver.PlatformName)
            .Select(entry => entry.Sampler)
            .ToList();

        Console.WriteLine($"[stress] WinUI 采样器行数={rows.Count}: {string.Join(", ", rows)}");

        var failures = 0;
        var attempts = 0;

        foreach (var row in rows)
        {
            for (var round = 0; round < 4; round++)
            {
                attempts++;

                // **失败发生的确切序列**：先点批量（十几行动画同时跑起来，demo 的 UI 线程正忙，而且这个工具栏按钮
                // 的 BringIntoView 已经把列表滚回了顶端），紧接着就去滚一行并驱动它。抽样点正是跑在这个位置。
                driver.Click("over.btn.start.all");

                try
                {
                    driver.ActivateSampler(row);
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.WriteLine($"[stress] 第 {attempts} 次失败：{row}{Screenshot(driver, row)} — {exception.Message}");
                }
            }
        }

        Console.WriteLine($"[stress] {attempts} 次里失败 {failures} 次。");
        Assert.AreEqual(0, failures, $"{attempts} 次里失败 {failures} 次");
    }

    private static string Screenshot(IDemoDriver driver, string row)
    {
        var path = Path.Combine(Path.GetTempPath(), $"veloxdev-stress-{row}-{DateTime.Now:HHmmss}.png");
        var written = driver.CaptureScreenshot(path);
        return written is null ? string.Empty : $" [screenshot: {written}]";
    }
}
