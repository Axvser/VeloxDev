using VeloxDev.AT.Drivers;
using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Suites;

/// <summary>
/// The Blazor end of the acceptance run: every scenario the demo's overshoot strip exposes, driven through a real click
/// in a real page and checked against the theory oracle.
/// </summary>
/// <remarks>
/// Two assertions matter, and they are different in kind. The peak one is a lower bound, because a UI observer samples
/// the curve on a timer and can miss the top of it — the bound it is held to already has that sampling loss subtracted,
/// and it sits above the target, so a run that never passes its target cannot satisfy it. The settle one is exact, and
/// it is gated on the demo reporting <c>done=1</c>: both curves cross their target again on the way back (Elastic seven
/// times in 1.1s), so waiting for "the value equals the target" would pass while the animation is still in flight.
/// <para>
/// This platform's wrinkle is that its adapter has no brush type, so the fill scenarios animate a CSS string: a frame
/// reads <c>rgba(r, g, b, a)</c> and only the two endpoints are the <c>#rrggbb</c> the caller passed in. Every colour
/// assertion here therefore goes through <see cref="CssColor"/> and is made per channel. The sixth scenario is a
/// substitution as well: with no brush to make non-solid, the demo drives a channel onto the 255 boundary instead,
/// which is the closest thing this platform can express — it still settles exactly on its target, and it still says
/// something no other scenario does, namely that the shared progress stops at the boundary instead of wrapping.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("AT.Blazor")]
public class BlazorOvershootSuite
{
    /// <summary>
    /// How far the settled value may sit from the target. This is the payload's own print precision and not a fudge
    /// factor: the readout formats with <c>F3</c>, so half a thousandth is as close as a reported number can be.
    /// </summary>
    private const double SettleTolerance = 5e-4;

    private static BlazorDemoDriver? _demo;

    public TestContext TestContext { get; set; } = null!;

    /// <summary>Start the demo once for the class: launching a server and a browser per assertion would dominate the run time.</summary>
    [ClassInitialize]
    public static void LaunchDemo(TestContext context)
    {
        // 未开门禁时什么都不做。测试方法各自的 RequireEnabled 会 Skip，不会把一次初始化失败当成门禁本身。
        if (!AtConfig.Enabled || !AtConfig.RunsOn(BlazorDemoDriver.PlatformName)) return;

        _demo = new BlazorDemoDriver();
        _demo.Launch();
    }

    /// <summary>Tear the demo down: the page is closed and the server's process tree is killed.</summary>
    [ClassCleanup]
    public static void CloseDemo()
    {
        _demo?.Dispose();
        _demo = null;
    }

    /// <summary>
    /// On a failure, photograph the page. The payload and the human readout sit in the same document, so the picture
    /// usually says outright whether the animation was wrong or the page was.
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
    public void Blazor_Back_OvershootsAndSettlesExactly() => AssertScalar(ScenarioId.Back);

    /// <summary>A translate on X under <c>Elastic.Out</c>.</summary>
    [TestMethod]
    public void Blazor_Elastic_OvershootsAndSettlesExactly() => AssertScalar(ScenarioId.Elastic);

    /// <summary>A width under <c>Elastic.Out</c>, the second scalar the payload tracks a peak for.</summary>
    [TestMethod]
    public void Blazor_Size_OvershootsAndSettlesExactly() => AssertScalar(ScenarioId.Size);

    /// <summary>
    /// A CSS fill under <c>Back.Out</c>. Every channel starts with headroom, so the shared progress takes all three
    /// past their targets together; had any channel been clamped on its own the hue would shift, which is the failure
    /// this looks for. The frames are <c>rgba(...)</c> and the endpoints <c>#rrggbb</c>, so both are read here.
    /// </summary>
    [TestMethod]
    public void Blazor_Color_OvershootsOnEveryChannelAndSettlesExactly()
    {
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
        var driver = Demo();
        var spec = driver.Scenarios.Single(scenario => scenario.Id == ScenarioId.Color);
        var span = spec.Color ?? throw new InvalidOperationException($"{spec} declares no colour span.");

        var run = driver.Run(spec);

        Assert.IsTrue(run.Settled.Done, $"The colour run never reported done=1. Last payload: {run.Settled.Raw}");

        var settledText = run.Settled.Text(span.CurrentKey);
        Assert.IsTrue(CssColor.TryParse(settledText, out var settled),
            $"The fill settled as '{settledText}', which is neither a #rrggbb nor an rgb()/rgba() colour.");

        Assert.AreEqual(span.Target, settled,
            $"The fill settled at {settled}, not at the target {span.Target}.");

        var extremes = ChannelExtremes(run, span.CurrentKey);
        Assert.IsTrue(extremes.HasValue,
            $"The colour run never reported a readable colour. {run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms.");

        var observed = extremes.GetValueOrDefault().Max;
        Assert.IsTrue(observed.R > span.Target.R && observed.G > span.Target.G && observed.B > span.Target.B,
            $"The fill peaked at {observed}, which does not exceed the target {span.Target} on every channel; "
            + $"the channels are no longer moving together. {run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms.");

        TestContext.WriteLine(
            $"{spec}: {span.Start} -> {observed} -> {settled} against the target {span.Target}, "
            + $"{run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms");
    }

    /// <summary>
    /// The <c>brush</c> scenario on this platform: a fill driven onto the 255 boundary. It stands in for the non-solid
    /// fill the other demos run — the browser adapter has no brush type to make non-solid — and it is worth asserting
    /// for what it does say. The target colour puts the red channel past the limit, so the shared progress has to stop
    /// at the boundary; a bare byte cast would wrap it (58 + 188 x 1.088 is about 263, which a cast turns into 7), and
    /// that wrap is exactly what the frames are checked for. The green channel overshoots its target and the blue one
    /// passes below its own, which is what "past the target" means for a channel that is being animated downwards.
    /// </summary>
    [TestMethod]
    public void Blazor_Brush_SaturatesAtTheBoundaryAndSettles()
    {
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
        var driver = Demo();
        var spec = driver.Scenarios.Single(scenario => scenario.Id == ScenarioId.Brush);
        var key = spec.BrushKey ?? throw new InvalidOperationException($"{spec} declares no brush field.");
        var span = spec.Color ?? throw new InvalidOperationException($"{spec} declares no colour span.");

        var run = driver.Run(spec);

        Assert.IsTrue(run.Settled.Done, $"The saturation run never reported done=1. Last payload: {run.Settled.Raw}");

        var settledText = run.Settled.Text(key);
        Assert.IsTrue(CssColor.TryParse(settledText, out var settled),
            $"The fill settled as '{settledText}', which is neither a #rrggbb nor an rgb()/rgba() colour.");

        Assert.AreEqual(span.Target, settled,
            $"The saturated fill settled at {settled}, not at the target {span.Target}.");

        var extremes = ChannelExtremes(run, key);
        Assert.IsTrue(extremes.HasValue,
            $"The saturation run never reported a readable colour. {run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms.");

        var (min, max) = extremes.GetValueOrDefault();

        // 撞上限的通道必须停在边界上，而且是"撞到"而不是"绕过"：没有钳位时会被裸转成个位数。
        Assert.AreEqual(byte.MaxValue, max.R,
            $"The red channel peaked at {max.R} rather than the 255 boundary, so the fill never reached the limit this "
            + $"scenario exists to test. {run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms.");

        Assert.IsTrue(max.G > span.Target.G,
            $"The green channel peaked at {max.G}, which does not exceed its target {span.Target.G}: the shared progress "
            + "no longer carries every channel past the target.");

        Assert.IsTrue(min.B < span.Target.B,
            $"The blue channel only reached {min.B}, which does not pass below its target {span.Target.B}: a channel "
            + "animated downwards has to overshoot downwards too.");

        foreach (var payload in run.Flight)
        {
            if (!CssColor.TryParse(payload.Text(key), out var sample)) continue;

            Assert.IsTrue(sample.R >= span.Start.R,
                $"A frame reported R={sample.R}, below the start {span.Start.R}: the overshoot wrapped the channel "
                + $"instead of stopping at the boundary. {payload.Raw}");
        }

        TestContext.WriteLine(
            $"{spec}: {span.Start} -> red {max.R} (boundary, never below {min.R}), green {max.G} (target {span.Target.G}), "
            + $"blue {min.B} (target {span.Target.B}) -> {settled}, {run.SampleCount} samples over {run.Settled.ElapsedMs:F0}ms");
    }

    /// <summary>
    /// The same Elastic run again and again, holding every single one to its bound. One run passing says the animation
    /// works; a run of them says the bound is not clearing it by luck, which is the difference between a test and a
    /// coin toss. The bound itself is never relaxed to accommodate a low sample — the readout interval is what would
    /// have to come down.
    /// </summary>
    [TestMethod]
    public void Blazor_Elastic_EveryRepeatedRunClearsItsBound()
    {
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
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
        DemoCatalog.RequireEnabled(BlazorDemoDriver.PlatformName);
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

    /// <summary>
    /// The per-channel extremes of a field the browser spells as a CSS colour, over every sample of the flight. Per
    /// channel rather than per sample for the reason the desktop suites give: the three channels move on one shared
    /// progress, so a channel's own extreme is what the frame at the animation's peak reports. The minimum is kept
    /// beside the maximum here because on this platform a channel is animated downwards as well — a wrap would show up
    /// as a minimum far below the start.
    /// </summary>
    private static (RgbColor Min, RgbColor Max)? ChannelExtremes(ScenarioRun run, string key)
    {
        RgbColor? min = null;
        RgbColor? max = null;

        foreach (var payload in run.Flight)
        {
            if (!payload.Has(key) || !CssColor.TryParse(payload.Text(key), out var color)) continue;

            min = min is null
                ? color
                : new RgbColor(Math.Min(min.Value.R, color.R), Math.Min(min.Value.G, color.G), Math.Min(min.Value.B, color.B));

            max = max is null
                ? color
                : new RgbColor(Math.Max(max.Value.R, color.R), Math.Max(max.Value.G, color.G), Math.Max(max.Value.B, color.B));
        }

        return min is null || max is null ? null : (min.Value, max.Value);
    }

    private static BlazorDemoDriver Demo() => _demo
        ?? throw new InvalidOperationException(
            $"{BlazorDemoDriver.PlatformName} was not launched. VELOXDEV_AT is set, so the demo build, its server or the browser is the problem.");

    /// <summary>Strip the characters a test name may contain but a file name may not.</summary>
    private static string Sanitize(string name)
        => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
