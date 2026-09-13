using VeloxDev.Timing;

namespace VeloxDev.Core.Test.Timing;

/// <summary>
/// The mode an animation-like consumer uses: the interval since the previous sample, owed to nobody.
/// </summary>
/// <remarks>
/// The sharpest thing pinned here is the absence of a post-resume spike. The frame loop it replaces measured
/// against a wall clock while a separate flag said "do not count", so the first frame after a pause carried the
/// whole pause as its delta. Sampling a source that does not advance while paused makes that impossible, and the
/// assertion only holds because the two clocks are the same one.
/// </remarks>
[TestClass]
public class UncompensatedTimeSamplerTests
{
    [TestMethod]
    public void TheFirstSamplePushesNothing()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        Assert.AreEqual(default(TimeSample), sampler.Sample());
    }

    [TestMethod]
    public void ReportsTheMeasuredIntervalAndAccumulatesTheTotal()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        source.Advance(TimeSpan.FromMilliseconds(8));
        var first = sampler.Sample();
        Assert.AreEqual(TimeSpan.FromMilliseconds(8), first.Delta);
        Assert.AreEqual(TimeSpan.FromMilliseconds(8), first.Total);
        Assert.AreEqual(0, first.Step, "an uncompensated sampler has no step numbering");
        Assert.AreEqual(source.Epoch, first.Epoch);

        source.Advance(TimeSpan.FromMilliseconds(5));
        var second = sampler.Sample();
        Assert.AreEqual(TimeSpan.FromMilliseconds(5), second.Delta, "only the interval since the previous sample");
        Assert.AreEqual(TimeSpan.FromMilliseconds(13), second.Total);
    }

    [TestMethod]
    public void NothingIsCarriedBetweenCalls()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        // 无偿的含义：迟到就是迟到，不欠不补。两次采样之间没有累计量，所以 Delta 就是各自的实测值。
        source.Advance(TimeSpan.FromMilliseconds(3));
        Assert.AreEqual(TimeSpan.FromMilliseconds(3), sampler.Sample().Delta);
        source.Advance(TimeSpan.FromMilliseconds(7));
        Assert.AreEqual(TimeSpan.FromMilliseconds(7), sampler.Sample().Delta);
    }

    [TestMethod]
    public void TheSumOfTheDeltasEqualsTheTotal()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        var sum = TimeSpan.Zero;
        foreach (var ms in new[] { 4, 9, 2, 16, 1, 7 })
        {
            source.Advance(TimeSpan.FromMilliseconds(ms));
            var sample = sampler.Sample();
            sum += sample.Delta;
            Assert.AreEqual(sum, sample.Total);
        }
    }

    [TestMethod]
    public void ARepeatedSampleWithoutAdvanceReportsNothing()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        source.Advance(TimeSpan.FromMilliseconds(6));
        Assert.AreEqual(TimeSpan.FromMilliseconds(6), sampler.Sample().Delta);

        // 同一格时钟内再采一次：不推进基准，于是没有时间可给——调用方据此跳过这一帧。
        Assert.AreEqual(default(TimeSample), sampler.Sample());
    }

    [TestMethod]
    public void APausedSourceContributesNothing()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        source.Advance(TimeSpan.FromMilliseconds(6));
        sampler.Sample();

        source.Pause();
        Assert.AreEqual(default(TimeSample), sampler.Sample(), "a paused source has not advanced, so there is no frame");
        Assert.IsFalse(source.IsAdvancing);
    }

    [TestMethod]
    public void ResumingDoesNotHandOverThePausedInterval()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        source.Advance(TimeSpan.FromMilliseconds(5));
        Assert.AreEqual(TimeSpan.FromMilliseconds(5), sampler.Sample().Delta);

        // 真实时间在暂停期间照样流逝，但总线的位置不动——所以它不会被欠下，也就无从追补。
        source.Pause();
        System.Threading.Thread.Sleep(60);
        Assert.AreEqual(default(TimeSample), sampler.Sample());

        source.Resume();
        source.Advance(TimeSpan.FromMilliseconds(5));
        var afterResume = sampler.Sample();

        Assert.AreEqual(TimeSpan.FromMilliseconds(5), afterResume.Delta, "only the frames since the resume");
        Assert.AreEqual(TimeSpan.FromMilliseconds(10), afterResume.Total, "the 60ms of pause never entered the total");
    }

    [TestMethod]
    public void ResetReAnchorsAndClearsTheTotal()
    {
        var source = new FakeTimeSource();
        var sampler = new UncompensatedTimeSampler(source);

        source.Advance(TimeSpan.FromMilliseconds(30));
        sampler.Sample();

        sampler.Reset();

        Assert.AreEqual(default(TimeSample), sampler.Sample(), "a reset re-anchors to the present");
        source.Advance(TimeSpan.FromMilliseconds(4));
        var sample = sampler.Sample();
        Assert.AreEqual(TimeSpan.FromMilliseconds(4), sample.Delta);
        Assert.AreEqual(TimeSpan.FromMilliseconds(4), sample.Total, "and the total restarts");
    }

    [TestMethod]
    public void ANullSourceIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new UncompensatedTimeSampler(null!));
    }

    [TestMethod]
    public void AgainstTheDefaultSourceTheDeltaTracksItsOwnClock()
    {
        // 替身证明了算术，这一条证明单位换算没错：默认源以 Stopwatch 频率计数，两者弄混会有上千倍偏差。
        var source = new TimeSourceCore();
        var sampler = new UncompensatedTimeSampler(source);

        System.Threading.Thread.Sleep(30);
        var sample = sampler.Sample();

        Assert.IsTrue(
            sample.Delta >= TimeSpan.FromMilliseconds(20) && sample.Delta <= TimeSpan.FromMilliseconds(200),
            $"a 30ms sleep should read as roughly 30ms, not {sample.Delta}");
        Assert.IsTrue(sample.Total >= sample.Delta);
    }
}
