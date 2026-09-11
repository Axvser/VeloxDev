namespace VeloxDev.AT.Theory;

/// <summary>
/// Pins the oracle itself. The peaks below are known constants, so a mistake in the curve or in the scan shows up
/// here rather than as a mysterious failure in a UI suite an hour later.
/// </summary>
[TestClass]
public class OvershootCurveTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(40);

    [TestMethod]
    public void BackOut_Peak_IsTheKnownConstant()
    {
        var (peak, at) = OvershootCurve.Scan(EaseKind.BackOut);

        Assert.AreEqual(1.1000041d, peak, 1e-6d);
        Assert.AreEqual(0.5801025d, at, 1e-4d);
    }

    [TestMethod]
    public void ElasticOut_Peak_IsTheKnownConstant()
    {
        var (peak, at) = OvershootCurve.Scan(EaseKind.ElasticOut);

        Assert.AreEqual(1.3730980d, peak, 1e-6d);
        Assert.AreEqual(0.1347400d, at, 1e-4d);
    }

    [TestMethod]
    public void SamplingLoss_IsWiderForTheSteeperPeak()
    {
        // Elastic peaks early, where the curve is steep, so a 40ms grid can miss far more of it than it can of Back,
        // whose peak sits where the curve is nearly flat. This is what makes Back the tight test and Elastic the
        // loose one, and why tightening Elastic means shortening the demo's readout interval rather than the bound.
        var back = OvershootCurve.SamplingLoss(EaseKind.BackOut, Interval, TimeSpan.FromMilliseconds(900));
        var elastic = OvershootCurve.SamplingLoss(EaseKind.ElasticOut, Interval, TimeSpan.FromMilliseconds(1100));

        Assert.AreEqual(0.0036d, back, 1e-4d);
        Assert.AreEqual(0.1353d, elastic, 1e-4d);
        Assert.IsTrue(elastic > back * 10d);
    }

    [TestMethod]
    public void Threshold_ForTheReferenceScenario_IsTheKnownValue()
    {
        var threshold = OvershootCurve.Threshold(EaseKind.BackOut, start: 0d, target: 300d, Interval, TimeSpan.FromMilliseconds(900));

        Assert.AreEqual(328.9d, threshold, 0.1d);
    }

    [TestMethod]
    public void Threshold_ClearsTheTarget_ForEveryDemosScenario()
    {
        // The anti-vacuity guard, exercised on the numbers the seven demos actually use. If any of these stopped
        // clearing its target, the corresponding UI assertion would pass without an overshoot happening.
        (EaseKind Kind, double Start, double Target, int DurationMs)[] scenarios =
        [
            (EaseKind.BackOut, 0d, 300d, 900),
            (EaseKind.ElasticOut, 0d, 300d, 1100),
            (EaseKind.ElasticOut, 80d, 220d, 1100),
            (EaseKind.ElasticOut, 0d, 220d, 1100),
            (EaseKind.ElasticOut, 38d, 188d, 1100),
            (EaseKind.BackOut, 60d, 220d, 900),
        ];

        foreach (var (kind, start, target, durationMs) in scenarios)
        {
            var threshold = OvershootCurve.Threshold(kind, start, target, Interval, TimeSpan.FromMilliseconds(durationMs));
            Assert.IsTrue(threshold > target,
                $"{kind} {start}->{target} clears {threshold:F3}, which is not above its target");
        }
    }

    [TestMethod]
    public void Threshold_RefusesAScenarioItCannotJudge()
    {
        // A wide interval over a short run misses the whole overshoot, so the guard must refuse rather than hand back
        // a threshold that any movement would satisfy.
        Assert.Throws<InvalidOperationException>(
            () => OvershootCurve.Threshold(EaseKind.ElasticOut, start: 0d, target: 1d, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    }
}
