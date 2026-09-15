using VeloxDev.Timing;

namespace VeloxDev.Core.Test.Timing;

/// <summary>
/// The half of <see cref="ITimeSource"/> a host that owns time supplies, and that the default source never
/// exercises: a clock whose position comes from somewhere else, and which can fall silent without anyone pausing
/// it.
/// </summary>
/// <remarks>
/// <see cref="TimeSourceContractTests"/> pins the default source's own behaviour. What is pinned here is the seam
/// itself — that a host can be built entirely outside Core, in the handful of lines a host would actually write, and
/// that the invariant <see cref="ITimeSource.IsAdvancing"/> states still holds on it. Both classes exist because the
/// two implementations reach the parking rule from opposite directions: the default one can only ever be stalled by
/// a control call, a host's can be stalled by nothing at all.
/// </remarks>
[TestClass]
public class HostTimeSourceTests
{
    /// <summary>The clock a pull host answers with; the test writes it, the source reads it.</summary>
    private sealed class HostClock
    {
        public long Stamp;
    }

    /// <summary>
    /// A host that answers "what time is it now", counting in milliseconds rather than the machine clock's unit —
    /// so a source that ignored the unit it was handed would be wrong by a factor, not by a rounding.
    /// </summary>
    private sealed class PullHostSource(HostClock host) : TimeSourceCore(() => host.Stamp, MillisecondsPerSecond)
    {
        public const long MillisecondsPerSecond = 1000L;
    }

    /// <summary>Where a push host delivers: the callback writes the stamp, the source reads it back.</summary>
    private sealed class HostFeed
    {
        public long Stamp;
    }

    /// <summary>
    /// A host that pushes its position from a callback, and reports when that callback stops — the shape the
    /// protected constructor and <c>SetHostFeeding</c> exist for.
    /// </summary>
    private sealed class PushHostSource(HostFeed feed)
        : TimeSourceCore(() => feed.Stamp, TimeSpan.TicksPerSecond)
    {
        public void Deliver(TimeSpan position) => feed.Stamp = position.Ticks;

        public void Stall() => SetHostFeeding(false);

        public void Feed() => SetHostFeeding(true);
    }

    [TestMethod]
    public void AHostSuppliesItsClockAndItsUnit()
    {
        var host = new HostClock();
        var source = new PullHostSource(host);

        Assert.AreEqual(PullHostSource.MillisecondsPerSecond, source.TicksPerSecond);
        Assert.IsTrue(source.IsAdvancing, "a host that has not reported a stall is feeding");

        host.Stamp = 500L;

        Assert.AreEqual(TimeSpan.FromMilliseconds(500), source.Position);
    }

    [TestMethod]
    public void AStalledHostFreezesTheClockWithoutPausingIt()
    {
        var feed = new HostFeed();
        var source = new PushHostSource(feed);
        source.Deliver(TimeSpan.FromMilliseconds(10));
        var before = source.Position;

        source.Stall();

        Assert.IsFalse(source.IsPaused, "the host fell silent; nobody called Pause");
        Assert.AreEqual(before, source.Position, "a stalled host's position does not move");
        Assert.IsFalse(source.IsAdvancing, "which is exactly what the invariant requires it to report");
    }

    [TestMethod]
    public void AStalledHostParksItsConsumers()
    {
        var feed = new HostFeed();
        var source = new PushHostSource(feed);

        source.Stall();

        // 这一条才是重点：谓词为假而 park 信号没装上，被 park 的等待会立刻返回、立刻重进——
        // 宿主时钟特有的失败模式，因为没有任何控制调用会来补上这个信号。
        Assert.IsFalse(
            source.WaitWhileStalledAsync().IsCompleted,
            "the park signal must agree with IsAdvancing even when the host, not a control call, stalled it");
    }

    [TestMethod]
    public void FeedingAgainReleasesAParkedConsumer()
    {
        var feed = new HostFeed();
        var source = new PushHostSource(feed);
        source.Stall();
        var parked = source.WaitWhileStalledAsync();

        source.Feed();

        Assert.IsTrue(source.IsAdvancing);
        Assert.IsTrue(parked.IsCompleted, "a consumer parked on a silent host must be released when it speaks again");
        Assert.IsTrue(source.WaitWhileStalledAsync().IsCompleted);
    }

    [TestMethod]
    public void AStallIsNotARebase()
    {
        var feed = new HostFeed();
        var source = new PushHostSource(feed);
        source.Deliver(TimeSpan.FromMilliseconds(40));
        var epochAtStall = source.Epoch;
        var positionAtStall = source.Position;

        source.Stall();
        source.Feed();

        Assert.AreEqual(epochAtStall, source.Epoch, "the clock resumes where it stopped, so nothing is invalidated");
        Assert.AreEqual(positionAtStall, source.Position);
    }

    [TestMethod]
    public void ReportingTheSameFeedStateTwiceWakesNobody()
    {
        var feed = new HostFeed();
        var source = new PushHostSource(feed);
        source.Stall();
        var parked = source.WaitWhileStalledAsync();

        source.Stall();

        Assert.IsFalse(parked.IsCompleted, "a repeat report has nothing to wake a consumer for");
    }

    [TestMethod]
    public void AHostMaySeekWhileItsFeedIsSilent()
    {
        var feed = new HostFeed();
        var source = new PushHostSource(feed);
        source.Stall();

        source.Seek(TimeSpan.FromSeconds(3));

        Assert.AreEqual(TimeSpan.FromSeconds(3), source.Position);
        Assert.IsFalse(source.IsAdvancing, "a seek is a nudge; it does not resume a host that is not feeding");
    }

    [TestMethod]
    public void ANullStampDelegateIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new NullStampSource());
    }

    [TestMethod]
    public void ANonPositiveTickUnitIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ZeroUnitSource());
    }

    private sealed class NullStampSource() : TimeSourceCore(null!, 1000L);

    private sealed class ZeroUnitSource() : TimeSourceCore(static () => 0L, 0L);
}
