using VeloxDev.Timing;

namespace VeloxDev.Core.Test.Timing;

/// <summary>
/// The contract that makes this sampler worth having: the number of steps delivered equals
/// <c>floor(elapsed / step)</c>, and it stays that way when the source jumps, when the clock stalls, and when the
/// per-call cap bites.
/// </summary>
/// <remarks>
/// Every assertion here is an exact integer comparison against a hand-driven clock. That is the point of the
/// <see cref="FakeTimeSource"/> rig: the behaviour under test is arithmetic, and a test that measured it against
/// real time could only ever assert a ratio.
/// </remarks>
[TestClass]
public class CompensatingTimeSamplerTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    /// <summary>A sampler with both bounds lifted, so the pure count invariant is what is being observed.</summary>
    private static CompensatingTimeSampler Unbounded(FakeTimeSource source)
        => new(source, Step) { MaxStepsPerCall = int.MaxValue, MaxPendingSteps = int.MaxValue };

    [TestMethod]
    public void DoesNotPushUntilAStepIsOwed()
    {
        var source = new FakeTimeSource();
        var sampler = Unbounded(source);

        Assert.AreEqual(0, sampler.Advance(out var sample));
        Assert.AreEqual(default(TimeSample), sample, "nothing owed must report an empty sample, not a zero-length step");

        source.Advance(TimeSpan.FromMilliseconds(9));
        Assert.AreEqual(0, sampler.Advance(out _));
        Assert.AreEqual(0, sampler.PendingSteps);
    }

    [TestMethod]
    public void CarriesTheSubStepRemainderAcrossCalls()
    {
        var source = new FakeTimeSource();
        var sampler = Unbounded(source);

        // 6ms 不够一步——按「每次取整、余数丢掉」的写法这 6ms 会永远消失。
        source.Advance(TimeSpan.FromMilliseconds(6));
        Assert.AreEqual(0, sampler.Advance(out _));

        // 再给 4ms，凑满第一步。余数被携带，所以这一步才存在。
        source.Advance(TimeSpan.FromMilliseconds(4));
        Assert.AreEqual(1, sampler.Advance(out var first));
        Assert.AreEqual(Step, first.Delta);
        Assert.AreEqual(1, first.Step);
        Assert.AreEqual(Step, first.Total);

        source.Advance(TimeSpan.FromMilliseconds(6));
        Assert.AreEqual(0, sampler.Advance(out _));
        source.Advance(TimeSpan.FromMilliseconds(4));
        Assert.AreEqual(1, sampler.Advance(out var second));
        Assert.AreEqual(2, second.Step);
        Assert.AreEqual(TimeSpan.FromMilliseconds(20), second.Total);
    }

    [TestMethod]
    public void TheDeliveredCountEqualsFloorOfElapsedOverStep()
    {
        var source = new FakeTimeSource();
        var sampler = Unbounded(source);

        // 一段不规则的推进序列：如果哪一步用了取整或者丢余数，累计值就会在这里对上不上。
        long[] advances = [3, 4, 6, 1, 11, 2, 7, 5, 9, 4, 13, 6, 2, 8];
        long elapsedTicks = 0;
        long delivered = 0;

        foreach (var ms in advances)
        {
            source.Advance(TimeSpan.FromMilliseconds(ms));
            elapsedTicks += TimeSpan.FromMilliseconds(ms).Ticks;

            var count = sampler.Advance(out var sample);
            delivered += count;

            Assert.AreEqual(
                elapsedTicks / Step.Ticks,
                delivered,
                $"after {ms}ms the delivered count must still be floor(elapsed/step)");

            if (count <= 0) continue;

            // 固定步长而不是实测增量：消费方每步拿到的间隔必须一样，否则积分出来的轨迹不可复现。
            Assert.AreEqual(Step, sample.Delta);
            Assert.AreEqual(delivered, sample.Step);

            // Total 由步数派生，所以它永远不吃余数——这正是它能当积分基准的原因。
            Assert.AreEqual(TimeSpan.FromTicks(delivered * Step.Ticks), sample.Total);
            Assert.IsTrue(
                source.Position - sample.Total < Step,
                $"the sub-step remainder must stay out of Total (position {source.Position}, total {sample.Total})");
        }
    }

    [TestMethod]
    public void TheCapDefersTheBurstInsteadOfDiscardingIt()
    {
        var source = new FakeTimeSource();
        var sampler = new CompensatingTimeSampler(source, Step) { MaxStepsPerCall = 4 };

        source.AdvanceSteps(10, Step);

        Assert.AreEqual(4, sampler.Advance(out _));
        Assert.AreEqual(6, sampler.PendingSteps, "the cap must leave the rest owed, not forgive it");

        // 时钟不再前进，欠账仍要补完。今天的 FixedUpdateLoop 正是在这个位置丢步：它推完一次就把
        // lastFixedUpdateTime 设为当前时间，超出的余数直接消失，于是推送总数永久少于墙钟。
        Assert.AreEqual(4, sampler.Advance(out _));
        Assert.AreEqual(2, sampler.Advance(out _));
        Assert.AreEqual(0, sampler.Advance(out _));
        Assert.AreEqual(0, sampler.PendingSteps);
        Assert.AreEqual(0, sampler.DroppedSteps, "deferring is not dropping");
    }

    [TestMethod]
    public void PendingStepsTellATruncationFromBeingCaughtUp()
    {
        var source = new FakeTimeSource();
        var sampler = new CompensatingTimeSampler(source, Step) { MaxStepsPerCall = 4 };

        // 刚好在上限内：推完之后没有欠账，与「被上限截断」必须能区分开——否则无从判断欠了多少。
        source.AdvanceSteps(4, Step);
        Assert.AreEqual(4, sampler.Advance(out _));
        Assert.AreEqual(0, sampler.PendingSteps);

        source.AdvanceSteps(6, Step);
        Assert.AreEqual(4, sampler.Advance(out _));
        Assert.AreEqual(2, sampler.PendingSteps);
    }

    [TestMethod]
    public void PastThePendingBoundTheDebtIsForgivenAndCounted()
    {
        var source = new FakeTimeSource();
        var sampler = new CompensatingTimeSampler(source, Step) { MaxStepsPerCall = 2, MaxPendingSteps = 10 };

        // 一次欠 50 步，远超上限。不设上限的话：被挂起过的机器会欠下几百万步，而按上限限速偿还期间，
        // 消费方要比直接宽恕落后得久得多。
        source.AdvanceSteps(50, Step);

        Assert.AreEqual(2, sampler.Advance(out _));
        Assert.AreEqual(40, sampler.DroppedSteps, "everything past the bound is forgiven, and counted");
        Assert.AreEqual(8, sampler.PendingSteps);

        var delivered = 2;
        while (sampler.Advance(out _) > 0) delivered += 2;

        Assert.AreEqual(10, delivered, "exactly the allowed debt is repaid, and no more");
        Assert.AreEqual(40, sampler.DroppedSteps);
        Assert.AreEqual(0, sampler.PendingSteps);
    }

    [TestMethod]
    public void ARebaseDiscardsTheDebtInsteadOfPayingIt()
    {
        var source = new FakeTimeSource();
        var sampler = Unbounded(source);

        source.AdvanceSteps(5, Step);
        Assert.AreEqual(5, sampler.Advance(out _));

        // 往回跳：这是 rebase。照常结算会还出负账——「次数永远精确」唯独经不起这一条。
        source.Seek(TimeSpan.Zero);
        Assert.AreEqual(0, sampler.Advance(out _));
        Assert.AreEqual(0, sampler.PendingSteps);

        // 步序号跨过 seek 保持连续，位置从原点重新攒起。
        source.AdvanceSteps(2, Step);
        Assert.AreEqual(2, sampler.Advance(out var sample));
        Assert.AreEqual(7, sample.Step);
    }

    [TestMethod]
    public void PausingDiscardsTheSubStepRemainderRatherThanBankingIt()
    {
        var source = new FakeTimeSource();
        var sampler = Unbounded(source);

        source.Advance(TimeSpan.FromMilliseconds(9));
        Assert.AreEqual(0, sampler.Advance(out _));

        // 暂停是一次 rebase。那 9ms 在暂停期间既不该被偿还，也不该攒到恢复之后——
        // 恢复后它不再是新基准下的一段时间。
        source.Pause();
        source.Resume();

        Assert.AreEqual(0, sampler.Advance(out _));
        source.Advance(TimeSpan.FromMilliseconds(10));
        Assert.AreEqual(1, sampler.Advance(out _));
    }

    [TestMethod]
    public void ChangingTheStepDropsTheRemainderMeasuredInTheOldStep()
    {
        var source = new FakeTimeSource();
        var sampler = Unbounded(source);

        source.Advance(TimeSpan.FromMilliseconds(9));
        Assert.AreEqual(0, sampler.Advance(out _));

        // 运行时换挡（SetFixedUpdateInterval）：9ms 是旧步长的九成，不是新步长的九成。
        sampler.Step = TimeSpan.FromMilliseconds(25);
        Assert.AreEqual(0, sampler.Advance(out _));

        source.Advance(TimeSpan.FromMilliseconds(25));
        Assert.AreEqual(1, sampler.Advance(out var sample));
        Assert.AreEqual(TimeSpan.FromMilliseconds(25), sample.Delta, "the reported delta follows the new step");
    }

    [TestMethod]
    public void ResetRePrimesTheClockAndClearsEveryCounter()
    {
        var source = new FakeTimeSource();
        var sampler = new CompensatingTimeSampler(source, Step) { MaxStepsPerCall = 1 };

        source.AdvanceSteps(5, Step);
        Assert.AreEqual(1, sampler.Advance(out _));
        Assert.IsTrue(sampler.PendingSteps > 0);

        sampler.Reset();

        Assert.AreEqual(0, sampler.PendingSteps);
        Assert.AreEqual(0, sampler.DroppedSteps);
        Assert.AreEqual(0, sampler.Advance(out _), "the step numbering restarts from the new anchor");
        source.AdvanceSteps(1, Step);
        Assert.AreEqual(1, sampler.Advance(out var sample));
        Assert.AreEqual(1, sample.Step);
    }

    [TestMethod]
    public void AStepOfZeroOrLessIsRejected()
    {
        var source = new FakeTimeSource();
        var sampler = new CompensatingTimeSampler(source, Step);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompensatingTimeSampler(source, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompensatingTimeSampler(source, TimeSpan.FromMilliseconds(-10)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sampler.Step = TimeSpan.Zero);
    }

    [TestMethod]
    public void BothCapsMustAllowAtLeastOne()
    {
        var source = new FakeTimeSource();
        var sampler = new CompensatingTimeSampler(source, Step);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sampler.MaxStepsPerCall = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sampler.MaxPendingSteps = 0);
    }

    [TestMethod]
    public void ANullSourceIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CompensatingTimeSampler(null!, Step));
    }

    [TestMethod]
    public void AgainstTheDefaultSourceTheStepsTrackItsOwnClock()
    {
        // 手驱动的替身证明了算术，这一条证明单位换算没有错：默认源以 Stopwatch 频率计数，而步长是 TimeSpan，
        // 两者一旦弄混就会出现上千倍的偏差，而那在替身上看不出来。
        var source = new TimeSourceCore();
        var sampler = new CompensatingTimeSampler(source, Step) { MaxStepsPerCall = 1024, MaxPendingSteps = 1024 };

        System.Threading.Thread.Sleep(60);

        long pushed = 0;
        int count;
        while ((count = sampler.Advance(out var sample)) > 0)
        {
            Assert.AreEqual(Step, sample.Delta);
            pushed += count;
        }

        // 推完之后不再有欠账，所以剩下的未成步余数必然小于一步。
        var pushedTime = TimeSpan.FromTicks(pushed * Step.Ticks);
        Assert.IsTrue(pushedTime <= source.Position, "steps must never run ahead of the source");
        Assert.IsTrue(
            source.Position - pushedTime < TimeSpan.FromMilliseconds(11),
            $"after draining, at most a sub-step may remain (position {source.Position}, pushed {pushedTime})");
    }
}
