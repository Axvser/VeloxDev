using VeloxDev.Timing;

namespace VeloxDev.Core.Test.Timing;

/// <summary>
/// The registration entry: Core's own implementations are what a lookup finds, and a platform replaces one by
/// registering over the same contract.
/// </summary>
/// <remarks>
/// Every test here files under a contract of its own and removes it in <c>finally</c>, the discipline
/// <c>InterpolatorCoreTests</c> established. That is not tidiness: overwriting <see cref="ITimeSourceControl"/> for
/// even one test would hand every concurrently running animation a source whose clock never moves, and an animation
/// parked on a frozen clock does not fail — it hangs.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class TimerCoreRegistryTests
{
    /// <summary>A contract nothing else registers, so installing and removing it cannot disturb anyone.</summary>
    private interface IMarkerSource : ITimeSourceControl { }

    private sealed class MarkerSource : FakeTimeSource, IMarkerSource { }

    private interface IMarkerSampler : ITimeSampler { }

    private sealed class MarkerSampler : IMarkerSampler
    {
        public void Reset() { }
    }

    [TestMethod]
    public void CoreProvidesADefaultSource()
    {
        // 平台什么都不注入时也必须有东西可用——这是「统一用 Core 默认实现」的那一半。
        Assert.IsInstanceOfType<TimeSourceCore>(TimerCore.CreateTimeSource<ITimeSourceControl>());
    }

    [TestMethod]
    public void CoreProvidesBothDefaultSamplers()
    {
        var source = new TimeSourceCore();

        Assert.IsInstanceOfType<UncompensatedTimeSampler>(TimerCore.CreateTimeSampler<IUncompensatedTimeSampler>(source));
        Assert.IsInstanceOfType<CompensatingTimeSampler>(TimerCore.CreateTimeSampler<ICompensatingTimeSampler>(source));
    }

    [TestMethod]
    public void TheDefaultFixedStepIsTheOneTheRegistryAdvertises()
    {
        var source = new TimeSourceCore();
        var sampler = TimerCore.CreateTimeSampler<ICompensatingTimeSampler>(source);

        Assert.AreEqual(
            TimeSpan.FromMilliseconds(TimerCore.DefaultFixedStepMilliseconds),
            sampler.Step);
    }

    [TestMethod]
    public void EveryLookupHandsBackAFreshInstance()
    {
        // 注册的是工厂不是单例：源和采样器都带状态，共享一个实例就等于共享一条时间轴和一格欠账。
        Assert.AreNotSame(
            TimerCore.CreateTimeSource<ITimeSourceControl>(),
            TimerCore.CreateTimeSource<ITimeSourceControl>());

        var source = new TimeSourceCore();
        Assert.AreNotSame(
            TimerCore.CreateTimeSampler<IUncompensatedTimeSampler>(source),
            TimerCore.CreateTimeSampler<IUncompensatedTimeSampler>(source));
    }

    [TestMethod]
    public void ARegistrationUnderAContractIsWhatThatContractResolvesTo()
    {
        TimerCore.RegisterTimeSource<IMarkerSource>(static () => new MarkerSource());
        try
        {
            Assert.IsInstanceOfType<MarkerSource>(TimerCore.CreateTimeSource<IMarkerSource>());
        }
        finally
        {
            Assert.IsTrue(TimerCore.UnregisterTimeSource<IMarkerSource>());
        }
    }

    [TestMethod]
    public void RegisteringTwiceUnderAContractKeepsTheLastOne()
    {
        TimerCore.RegisterTimeSource<IMarkerSource>(static () => new MarkerSource());
        var replacement = new MarkerSource();
        TimerCore.RegisterTimeSource<IMarkerSource>(() => replacement);
        try
        {
            // 末位胜出，与 InterpolatorCore.RegisterInterpolator 同一条规则：平台后注入的一定生效。
            Assert.AreSame(replacement, TimerCore.CreateTimeSource<IMarkerSource>());
        }
        finally
        {
            TimerCore.UnregisterTimeSource<IMarkerSource>();
        }
    }

    [TestMethod]
    public void ASamplerRegistrationUnderAContractIsWhatThatContractResolvesTo()
    {
        TimerCore.RegisterTimeSampler<IMarkerSampler>(static _ => new MarkerSampler());
        try
        {
            var source = new TimeSourceCore();
            Assert.IsInstanceOfType<MarkerSampler>(TimerCore.CreateTimeSampler<IMarkerSampler>(source));
        }
        finally
        {
            Assert.IsTrue(TimerCore.UnregisterTimeSampler<IMarkerSampler>());
        }
    }

    [TestMethod]
    public void RegisteringUnderAnImplementationTypeIsNotWhatAContractLookupFinds()
    {
        // 这条规则是有代价的、所以要钉住：把实现类型当 key 注册等于存到一个没人会看的地方。
        // 契约查找只按契约找，不做「扫一遍看谁可赋值」的兜底——那种兜底在多条注册之间只能任选其一，
        // 而字典顺序是未定义的，同一个查找会在不同运行里给出不同实现。
        TimerCore.RegisterTimeSource<MarkerSource>(static () => new MarkerSource());
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => TimerCore.CreateTimeSource<IMarkerSource>());
        }
        finally
        {
            TimerCore.UnregisterTimeSource<MarkerSource>();
        }
    }

    [TestMethod]
    public void AnUnregisteredContractThrows()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => TimerCore.CreateTimeSource<IMarkerSource>());
        Assert.ThrowsExactly<InvalidOperationException>(() => TimerCore.CreateTimeSampler<IMarkerSampler>(new TimeSourceCore()));
    }

    [TestMethod]
    public void UnregisteringLeavesTheContractUnresolvableAgain()
    {
        TimerCore.RegisterTimeSource<IMarkerSource>(static () => new MarkerSource());
        Assert.IsTrue(TimerCore.UnregisterTimeSource<IMarkerSource>());

        Assert.IsFalse(TimerCore.UnregisterTimeSource<IMarkerSource>(), "a second removal has nothing to remove");
        Assert.ThrowsExactly<InvalidOperationException>(() => TimerCore.CreateTimeSource<IMarkerSource>());
    }

    [TestMethod]
    public void ANullFactoryIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TimerCore.RegisterTimeSource<IMarkerSource>(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TimerCore.RegisterTimeSampler<IMarkerSampler>(null!));
    }

    [TestMethod]
    public void ANullSourceIsRejectedByASamplerLookup()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TimerCore.CreateTimeSampler<IUncompensatedTimeSampler>(null!));
    }
}
