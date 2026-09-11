using VeloxDev.AT.Drivers;
using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Suites;

/// <summary>
/// The Avalonia end of the acceptance run: every scenario the demo's overshoot strip exposes, driven through UI
/// Automation and checked against the theory oracle.
/// </summary>
/// <remarks>
/// Two assertions matter, and they are different in kind. The peak one is a lower bound, because a UI observer samples
/// the curve on a timer and can miss the top of it — the bound it is held to already has that sampling loss subtracted,
/// and it sits above the target, so a run that never passes its target cannot satisfy it. The settle one is exact, and
/// it is gated on the demo reporting <c>done=1</c>: both curves cross their target again on the way back (Elastic seven
/// times in 1.1s), so waiting for "the value equals the target" would pass while the animation is still in flight.
/// </remarks>
[TestClass]
[TestCategory("AT.Avalonia")]
public class AvaloniaOvershootSuite
{
    /// <summary>
    /// How far the settled value may sit from the target. This is the payload's own print precision and not a fudge
    /// factor: the readout formats with <c>F3</c>, so half a thousandth is as close as a reported number can be.
    /// </summary>
    private const double SettleTolerance = 5e-4;

    private static AvaloniaDemoDriver? _demo;

    public TestContext TestContext { get; set; } = null!;

    /// <summary>Start the demo once for the class: launching a window per assertion would dominate the run time.</summary>
    [ClassInitialize]
    public static void LaunchDemo(TestContext context)
    {
        // 未开门禁时什么都不做。测试方法各自的 RequireEnabled 会 Skip，不会把一次初始化失败当成门禁本身。
        if (!AtConfig.Enabled || !AtConfig.RunsOn(AvaloniaDemoDriver.PlatformName)) return;

        _demo = new AvaloniaDemoDriver();
        _demo.Launch();
    }

    /// <summary>Tear the demo down; the job object covers the case where this never runs.</summary>
    [ClassCleanup]
    public static void CloseDemo()
    {
        _demo?.Dispose();
        _demo = null;
    }

    /// <summary>
    /// On a failure, photograph the demo. The payload and the human readout sit in the same window, so the picture
    /// usually says outright whether the animation was wrong or the surface was.
    /// </summary>
    [TestCleanup]
    public void CaptureFailure()
    {
        if (_demo is null || TestContext.CurrentTestOutcome != UnitTestOutcome.Failed) return;

        var path = Path.Combine(
            AtConfig.RepositoryRoot, "Tests", "VeloxDev.AT", "TestResults", "screenshots", $"{Sanitize(TestContext.TestName)}.png");

        var written = _demo.CaptureScreenshot(path);
        if (written is not null) TestContext.AddResultFile(written);
    }

    /// <summary>A translate on X under <c>Back.Out</c>.</summary>
    [TestMethod]
    public void Avalonia_Back_OvershootsAndSettlesExactly() => AssertScalar(ScenarioId.Back);

    /// <summary>A translate on X under <c>Elastic.Out</c>.</summary>
    [TestMethod]
    public void Avalonia_Elastic_OvershootsAndSettlesExactly() => AssertScalar(ScenarioId.Elastic);

    /// <summary>A width under <c>Elastic.Out</c>, the second scalar the payload tracks a peak for.</summary>
    [TestMethod]
    public void Avalonia_Size_OvershootsAndSettlesExactly() => AssertScalar(ScenarioId.Size);

    /// <summary>
    /// A solid fill under <c>Back.Out</c>. Every channel starts with headroom, so the shared progress takes all three
    /// past their targets together; had any channel been clamped on its own the hue would shift, which is the failure
    /// this looks for.
    /// </summary>
    /// <remarks>
    /// The colour is read out of whatever the payload writes as <c>#rrggbb</c> and never out of the brush's type name.
    /// The demo reports a colour for any <c>ISolidColorBrush</c>, deliberately, because a fill declared in XAML is an
    /// <c>ImmutableSolidColorBrush</c> while the animated one is a <c>SolidColorBrush</c> — pinning a concrete subtype
    /// here would fail the moment the two disagreed about which one a given frame used.
    /// </remarks>
    [TestMethod]
    public void Avalonia_Color_OvershootsOnEveryChannelAndSettlesExactly()
    {
        DemoCatalog.RequireEnabled(AvaloniaDemoDriver.PlatformName);
        var driver = Demo();
        var spec = driver.Scenarios.Single(scenario => scenario.Id == ScenarioId.Color);
        var span = spec.Color ?? throw new InvalidOperationException($"{spec} declares no colour span.");

        var run = driver.Run(spec);

        Assert.IsTrue(run.Settled.Done, $"The colour run never reported done=1. Last payload: {run.Settled.Raw}");

        Assert.IsTrue(RgbColor.TryParse(run.Settled.Text(span.CurrentKey), out var settled),
            $"The fill settled as '{run.Settled.Text(span.CurrentKey)}' rather than as a #rrggbb colour. {run.Summary(span.CurrentKey)}");

        Assert.IsTrue(settled == span.Target,
            $"The fill settled at {settled}, not at the target {span.Target}.");

        var peak = run.MaxColor(span.CurrentKey);
        Assert.IsTrue(peak.HasValue, $"The colour run never reported a #rrggbb value. {run.Summary(span.CurrentKey)}");

        var observed = peak.GetValueOrDefault();
        Assert.IsTrue(observed.R > span.Target.R && observed.G > span.Target.G && observed.B > span.Target.B,
            $"The fill peaked at {observed}, which does not exceed the target {span.Target} on every channel; "
            + "the channels are no longer moving together. " + run.Summary(span.CurrentKey));

        TestContext.WriteLine(
            $"{spec}: {span.Start} -> {observed} -> {settled} against the target {span.Target}, "
            + $"{run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms");
    }

    /// <summary>
    /// A non-solid fill. It takes the blended-brush path, whose factor is a fraction and therefore stops at either end
    /// instead of overshooting — so what is worth asserting is that the fill stayed a brush throughout and never fell
    /// back to none, and that the run still reached its end.
    /// </summary>
    [TestMethod]
    public void Avalonia_Brush_StaysNonSolidThroughoutAndSettles()
    {
        DemoCatalog.RequireEnabled(AvaloniaDemoDriver.PlatformName);
        var driver = Demo();
        var spec = driver.Scenarios.Single(scenario => scenario.Id == ScenarioId.Brush);
        var key = spec.BrushKey ?? throw new InvalidOperationException($"{spec} declares no brush field.");

        var run = driver.Run(spec);

        Assert.IsTrue(run.Settled.Done, $"The brush run never reported done=1. Last payload: {run.Settled.Raw}");

        // 纯色会被负载写成 #rrggbb；这里必须仍然是一个画刷类型名，说明走的是非纯色那条路径。
        var settled = run.Settled.Text(key);
        Assert.IsFalse(RgbColor.TryParse(settled, out _),
            $"The fill settled as the solid colour {settled}; the non-solid scenario should have stayed a gradient.");

        foreach (var payload in run.Flight)
        {
            Assert.AreNotEqual("none", payload.Text(key),
                $"The fill disappeared mid-flight, so the brush sampler dropped it: {payload.Raw}");
        }

        TestContext.WriteLine(
            $"{spec}: a non-solid fill throughout, settled on {settled}, "
            + $"{run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms");
    }

    /// <summary>
    /// The same Elastic run again and again, holding every single one to its bound. One run passing says the animation
    /// works; a run of them says the bound is not clearing it by luck, which is the difference between a test and a
    /// coin toss. The bound itself is never relaxed to accommodate a low sample — the readout interval is what would
    /// have to come down.
    /// </summary>
    [TestMethod]
    public void Avalonia_Elastic_EveryRepeatedRunClearsItsBound()
    {
        DemoCatalog.RequireEnabled(AvaloniaDemoDriver.PlatformName);
        var driver = Demo();
        var spec = driver.Scenarios.Single(scenario => scenario.Id == ScenarioId.Elastic);
        var scalar = spec.Scalar ?? throw new InvalidOperationException($"{spec} declares no scalar target.");
        var threshold = spec.OvershootThreshold;

        var repeats = RepeatCount;
        var peaks = new List<double>(repeats);

        for (var attempt = 1; attempt <= repeats; attempt++)
        {
            var run = driver.Run(spec);
            var peak = run.Max(scalar.PeakKey);
            peaks.Add(peak);

            Assert.IsTrue(peak >= threshold,
                $"Run {attempt} of {repeats} peaked at {peak:F3}, below the bound of {threshold:F3}. {run.Summary(scalar.PeakKey)}");
        }

        TestContext.WriteLine(
            $"Elastic {scalar.Start:F0}->{scalar.Target:F0} over {spec.Duration.TotalMilliseconds:F0}ms, {repeats} runs: "
            + $"min {peaks.Min():F3}, max {peaks.Max():F3}, mean {peaks.Average():F3}, bound {threshold:F3}. "
            + $"All peaks: {string.Join(", ", peaks.Select(peak => peak.ToString("F3")))}");
    }

    /// <summary>
    /// How many times the repeat run fires. Five by default so an ordinary suite stays quick; set
    /// <c>VELOXDEV_AT_REPEATS=20</c> to calibrate the bound against a distribution rather than a single sample.
    /// </summary>
    private static int RepeatCount
    {
        get
        {
            var requested = Environment.GetEnvironmentVariable("VELOXDEV_AT_REPEATS");
            return int.TryParse(requested, out var count) && count > 0 ? count : 5;
        }
    }

    /// <summary>
    /// Run a scalar scenario and hold it to both bounds. The threshold check comes first and deliberately: it is the
    /// guard that says the bound is worth asserting at all.
    /// </summary>
    private void AssertScalar(ScenarioId id)
    {
        DemoCatalog.RequireEnabled(AvaloniaDemoDriver.PlatformName);
        var driver = Demo();
        var spec = driver.Scenarios.Single(scenario => scenario.Id == id);
        var scalar = spec.Scalar ?? throw new InvalidOperationException($"{spec} declares no scalar target.");
        var threshold = spec.OvershootThreshold;

        // 阈值必须高于目标，否则"过冲"与"根本没动"无法区分 —— 这种断言即使动画没跑过目标也会通过。
        Assert.IsTrue(threshold > scalar.Target,
            $"{spec}: the bound {threshold:F3} does not clear the target {scalar.Target:F3}, so the assertion cannot tell an overshoot from no movement.");

        var run = driver.Run(spec);

        Assert.IsTrue(run.Settled.Done, $"{spec}: the run never reported done=1. Last payload: {run.Settled.Raw}");

        // 终点只在 demo 自己说 done=1 时才断言：载荷中途会多次再穿过目标，等"值等于目标"会在动画还在飞时通过。
        var settled = run.Settled.Number(scalar.CurrentKey);
        Assert.IsTrue(Math.Abs(settled - scalar.Target) <= SettleTolerance,
            $"{spec}: settled at {settled:F3} instead of {scalar.Target:F3} once done=1. {run.Summary(scalar.CurrentKey)}");

        var peak = run.Max(scalar.PeakKey);
        Assert.IsTrue(peak >= threshold,
            $"{spec}: peaked at {peak:F3}, below the bound of {threshold:F3}. {run.Summary(scalar.PeakKey)}");

        TestContext.WriteLine(
            $"{spec}: {scalar.Start:F3} -> peak {peak:F3} -> settled {settled:F3}, target {scalar.Target:F3}, bound {threshold:F3}, "
            + $"{run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms");
    }

    private static AvaloniaDemoDriver Demo() => _demo
        ?? throw new InvalidOperationException(
            $"{AvaloniaDemoDriver.PlatformName} was not launched. VELOXDEV_AT is set, so the demo build or its path is the problem.");

    /// <summary>Strip the characters a test name may contain but a file name may not.</summary>
    private static string Sanitize(string name)
        => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
