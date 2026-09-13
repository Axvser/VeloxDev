using VeloxDev.Timing;

namespace VeloxDev.Core.Test.Timing;

/// <summary>
/// The default source's own contract: the park signal, the advancing predicate, and the rebase counter.
/// </summary>
/// <remarks>
/// The pair asserted most carefully here is <see cref="ITimeSource.IsAdvancing"/> and
/// <see cref="ITimeSource.WaitWhileStalledAsync"/>. They are the two halves of one rule — park while the clock is
/// stalled — and if they ever disagree a consumer spinning on <c>while (!IsAdvancing)</c> burns a core instead of
/// parking, with no exception and no frame to show for it.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class TimeSourceContractTests
{
    [TestMethod]
    public void WaitWhileStalledIsAlreadyCompletedWhenAdvancing()
    {
        var source = new TimeSourceCore();

        Assert.IsTrue(source.IsAdvancing);
        Assert.IsTrue(source.WaitWhileStalledAsync().IsCompleted, "a running consumer must not park");
    }

    [TestMethod]
    public async Task WaitWhileStalledParksUntilResume()
    {
        var source = new TimeSourceCore();
        source.Pause();

        var woke = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            await source.WaitWhileStalledAsync();
            woke.TrySetResult(true);
        });

        await Task.Delay(50);
        Assert.IsFalse(woke.Task.IsCompleted, "nothing may wake a parked consumer while the clock is stalled");

        source.Resume();
        await woke.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task WakeLetsAParkedConsumerRedrawWithoutResuming()
    {
        var source = new TimeSourceCore();
        source.Pause();

        var woke = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            await source.WaitWhileStalledAsync();
            woke.TrySetResult(true);
        });

        await Task.Delay(50);
        Assert.IsFalse(woke.Task.IsCompleted);

        // 暂停中 seek 只会摇一次：消费者醒来重画新位置，然后重新 park。不摇的话新位置要等到恢复才可见。
        source.Seek(TimeSpan.FromSeconds(1));
        await woke.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(source.IsPaused, "a nudge must not resume the clock");
        Assert.AreEqual(TimeSpan.FromSeconds(1), source.Position);
    }

    [TestMethod]
    public async Task CancellationEndsAParkedWait()
    {
        var source = new TimeSourceCore();
        source.Pause();

        using var cts = new CancellationTokenSource();
        var observed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            try
            {
                await source.WaitWhileStalledAsync(cts.Token);
                observed.TrySetResult(null);
            }
            catch (OperationCanceledException ex)
            {
                observed.TrySetResult(ex);
            }
        });

        await Task.Delay(50);
        cts.Cancel();

        // 没有这一条，一次 Stop 会永远等不到那个「不会来的恢复」——被 park 的等待根本不知道令牌的存在。
        var exception = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsNotNull(exception, "a stop must reach a parked consumer rather than waiting for a resume");
    }

    [TestMethod]
    public void AZeroRateIsNotAPauseButIsStillStalled()
    {
        var source = new TimeSourceCore();
        source.SetRate(0d);

        Assert.IsFalse(source.IsPaused, "a zero rate is not a pause");
        Assert.AreEqual(0d, source.Rate);
        Assert.IsFalse(source.IsAdvancing, "but it is not advancing either");

        // 这一条是整个配对的意义所在：消费方按 IsAdvancing 判断要不要 park，park 信号就必须同样认为它在停摆。
        // 否则 while (!IsAdvancing) await WaitWhileStalledAsync() 会立刻返回、立刻重进，热转一整核。
        Assert.IsFalse(
            source.WaitWhileStalledAsync().IsCompleted,
            "the park signal must agree with IsAdvancing, or a stalled consumer spins");
    }

    [TestMethod]
    public void ANonZeroRateStartsAFrozenClockAgain()
    {
        var source = new TimeSourceCore();
        source.SetRate(0d);
        var stalled = source.WaitWhileStalledAsync();

        source.SetRate(1d);

        Assert.IsTrue(source.IsAdvancing);
        Assert.IsTrue(stalled.IsCompleted, "the consumer waiting on the frozen clock must be released");
        Assert.IsTrue(source.WaitWhileStalledAsync().IsCompleted);
    }

    [TestMethod]
    public void ResumingAfterAZeroRateLiftsThePauseWithoutStartingTheClock()
    {
        var source = new TimeSourceCore();
        source.SetRate(0d);
        source.Pause();

        source.Resume();

        Assert.IsFalse(source.IsPaused, "the pause is lifted");
        Assert.AreEqual(0d, source.Rate, "but the rate was never restored");
        Assert.IsFalse(source.IsAdvancing, "so the clock is still frozen, and a consumer still parks");
    }

    [TestMethod]
    public void APausedSourceDoesNotAdvance()
    {
        var source = new TimeSourceCore();

        // 必须先暂停再读数：Ticks 是活的，两次读之间时钟照走，那样量到的是读取间隔而不是暂停期间。
        source.Pause();
        var ticksAtPause = source.Ticks;
        var positionAtPause = source.Position;

        System.Threading.Thread.Sleep(40);

        Assert.AreEqual(ticksAtPause, source.Ticks, "the time spent stalled is excluded by construction");
        Assert.AreEqual(positionAtPause, source.Position);
    }

    [TestMethod]
    public void AZeroRateFreezesThePosition()
    {
        var source = new TimeSourceCore();
        source.SetRate(0d);
        var before = source.Ticks;

        System.Threading.Thread.Sleep(40);

        Assert.AreEqual(before, source.Ticks);
    }

    [TestMethod]
    public void EveryRebaseBumpsTheEpochAndARepeatedPauseDoesNot()
    {
        var source = new TimeSourceCore();
        var start = source.Epoch;

        source.Pause();
        var afterPause = source.Epoch;
        Assert.IsTrue(afterPause > start, "a pause is a rebase");

        // 幂等：重复暂停什么都不改，所以也不该让消费者的累计基准失效。
        source.Pause();
        Assert.AreEqual(afterPause, source.Epoch, "pausing an already paused clock rebases nothing");

        source.Resume();
        var afterResume = source.Epoch;
        Assert.IsTrue(afterResume > afterPause, "a resume is a rebase");

        source.SetRate(2d);
        var afterRate = source.Epoch;
        Assert.IsTrue(afterRate > afterResume, "a rate change is a rebase");

        source.Seek(TimeSpan.FromMilliseconds(10));
        Assert.IsTrue(source.Epoch > afterRate, "a seek is a rebase");
    }

    [TestMethod]
    public void PausingAndResumingLeavesTheRemainingDurationUntouched()
    {
        var source = new TimeSourceCore();

        System.Threading.Thread.Sleep(20);
        source.Pause();
        var atPause = source.Position;

        System.Threading.Thread.Sleep(40);

        Assert.AreEqual(atPause, source.Position, "nothing accrues while paused");
        source.Resume();
        Assert.IsTrue(source.IsAdvancing);
    }

    [TestMethod]
    public void PositionAndTicksUseTheUnitTheSourcePublishes()
    {
        var source = new TimeSourceCore();
        Assert.AreEqual(TimeConversion.DefaultTicksPerSecond, source.TicksPerSecond);

        // 换算走发布的单位而不是某个框架时钟的频率——注入源可能以别的单位计数。
        var oneSecond = TimeConversion.SpanToTicks(TimeSpan.FromSeconds(1), source.TicksPerSecond);
        Assert.AreEqual(TimeSpan.FromSeconds(1), TimeConversion.TicksToTimeSpan(oneSecond, source.TicksPerSecond));

        // 两者差一个原点：Ticks 是自机器时钟起点的绝对值（锚点运算的基准），Position 是自本源原点的相对值。
        // 一个刚刚构造出来的源，前者很大而后者接近零——这个区别正是锚点运算只需要相对量、
        // 而消费者报告时间时只需要相对量的原因。
        Assert.IsTrue(source.Ticks > oneSecond, "Ticks is absolute, measured from the machine's clock");
        Assert.IsTrue(source.Position < TimeSpan.FromSeconds(1), "Position is relative to this source's origin");
    }
}
