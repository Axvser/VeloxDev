using System.Linq.Expressions;
using VeloxDev.TimeLine;
using VeloxDev.Timing;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TimeLine;

/// <summary>
/// The frame loop wired to the shared time layer: the channel owns a bus, the loops park on it, and a consumer can
/// be anchored to the same clock.
/// </summary>
/// <remarks>
/// The centre of this file is <see cref="PausingAChannelStopsItsFramesAndTheAnimationAnchoredToIt"/> — one
/// <c>Pause()</c> stopping both halves is the whole point of routing the loop through a shared transport, and
/// nothing short of running both at once demonstrates it.
/// <para>
/// Not parallelized: <see cref="TickManager"/> is process-wide static state and these tests start real
/// channels on it.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class TickableBusTests
{
    private readonly List<string> _channels = [];

    private string Channel(string name)
    {
        _channels.Add(name);
        return name;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        foreach (var channel in _channels)
        {
            if (TickManager.IsRunning(channel)) await TickManager.StopAsync(channel);
        }
        _channels.Clear();
    }

    /// <summary>Counts the callbacks it receives. The smallest thing a channel can drive.</summary>
    private sealed class PushCounter : ITickable
    {
        private int _updates;
        private int _fixedUpdates;

        public int Updates => Volatile.Read(ref _updates);

        public int FixedUpdates => Volatile.Read(ref _fixedUpdates);

        public void InitializeTickable() { }

        public void CloseTickable() { }

        public void InvokeAwake() { }

        public void InvokeStart() { }

        public void InvokeUpdate(FrameEventArgs e) => Interlocked.Increment(ref _updates);

        public void InvokeLateUpdate(FrameEventArgs e) { }

        public void InvokeFixedUpdate(FrameEventArgs e) => Interlocked.Increment(ref _fixedUpdates);
    }

    private sealed class Target
    {
        public double Value { get; set; }
    }

    private sealed class ImmediateInspector : ImmediateHost
    {
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    private sealed class TestTransition : TransitionCore<
        Target, StateCore, TransitionEffectCore, TestInterpolator, ImmediateInspector, TestInterpreter, NonPriority>
    {
        public static TestTransition Create() => TransitionCore.Create<TestTransition>();

        public TestTransition Property<TValue>(Expression<Func<Target, TValue>> lambda, TValue value)
        {
            state.SetValue(lambda, value);
            return this;
        }

        public TestTransition Effect(TransitionEffectCore effect)
            => CoreEffect<TestTransition, TransitionEffectCore>(effect);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [TestMethod]
    public void BusIsNullForAChannelThatWasNeverStarted()
    {
        // 查询不该顺手创建一个渠道：没能启动的渠道没有 transport 可给。
        Assert.IsNull(TickManager.Bus("never-started-" + Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    public async Task BusIsStableForAStartedChannel()
    {
        var channel = Channel("bus-stable");
        TickManager.Start(channel);

        var first = TickManager.Bus(channel);
        var second = TickManager.Bus(channel);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second, "one channel is one transport, for everything anchored to it");
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task PausingAChannelStopsItsFramesAndResumingRestartsThem()
    {
        var channel = Channel("pause-frames");
        TickManager.Start(channel);

        Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > 2, 3000),
            "the channel must be pumping frames");

        TickManager.Pause(channel);
        Assert.IsTrue(TickManager.IsPaused(channel));

        await Task.Delay(60); // 让已在途的一帧落完，之后的读数才是暂停期间的
        var whilePaused = TickManager.TotalFrames(channel);

        await Task.Delay(200);
        Assert.AreEqual(whilePaused, TickManager.TotalFrames(channel), "a paused channel must not pump");

        // 旧实现靠每 10ms 醒来轮询暂停标志，暂停中的循环仍然活着；这里改为 park 在总线上，
        // 所以「线程还活着吗」不能再用「最近有没有活动」来判断。
        Assert.IsTrue(TickManager.IsUpdateThreadAlive(channel),
            "a parked loop is alive, not dead — the liveness query has to account for the stalled clock");
        Assert.IsTrue(TickManager.IsFixedUpdateThreadAlive(channel));

        TickManager.Resume(channel);
        Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > whilePaused, 3000),
            "frames must resume");
    }

    [TestMethod]
    public async Task PausingAChannelStopsItsFramesAndTheAnimationAnchoredToIt()
    {
        var channel = Channel("acceptance");
        TickManager.Start(channel);

        var bus = TickManager.Bus(channel);
        Assert.IsNotNull(bus, "a started channel must expose its transport");

        var target = new Target();
        TestTransition.Create()
            .Property(t => t.Value, 1d)
            .Effect(new TransitionEffectCore { Duration = TimeSpan.FromSeconds(30), FPS = 60 })
            .Execute(target, bus!); // 锚到渠道的同一条 transport

        try
        {
            Assert.IsTrue(await WaitUntilAsync(() => target.Value > 0.01d, 3000),
                "the animation must be running against the channel's clock");
            Assert.IsTrue(TickManager.TotalFrames(channel) > 2);

            TickManager.Pause(channel);
            await Task.Delay(60); // 在途的帧落完

            var framesAtPause = TickManager.TotalFrames(channel);
            var valueAtPause = target.Value;

            await Task.Delay(250);

            // 一次 Pause 同时停掉两半——这就是「接线同时接动画和帧循环」的可执行定义。
            Assert.IsTrue(bus!.IsPaused, "the channel's bus is the one the animation is anchored to");
            Assert.AreEqual(framesAtPause, TickManager.TotalFrames(channel), "no frames while paused");
            Assert.AreEqual(valueAtPause, target.Value, "no animation progress while paused");

            TickManager.Resume(channel);

            Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > framesAtPause, 3000),
                "frames must resume");
            Assert.IsTrue(await WaitUntilAsync(() => target.Value > valueAtPause, 3000),
                "the animation must resume");
        }
        finally
        {
            TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    [TestMethod]
    public async Task TheChannelsRateScalesTheAnimationButNotTheFrameCadence()
    {
        var channel = Channel("rate");
        TickManager.Start(channel);

        var bus = TickManager.Bus(channel);
        Assert.IsNotNull(bus);

        var target = new Target();
        TestTransition.Create()
            .Property(t => t.Value, 1d)
            .Effect(new TransitionEffectCore { Duration = TimeSpan.FromSeconds(30), FPS = 60 })
            .Execute(target, bus!);

        try
        {
            // SetTimeScale 现在就是总线的 Rate，逐字生效：钳制和静默忽略都没有了。
            TickManager.SetTimeScale(4f, channel);

            Assert.AreEqual(4f, TickManager.TimeScale(channel));
            Assert.AreEqual(4f, (float)bus!.Rate);

            var before = target.Value;
            await Task.Delay(120);

            // 4 倍速下，这段真实时间内动画走的进度是四倍。目标帧率约束的是采样节奏而不是虚拟时钟，
            // 所以帧回调的数量不跟着变——这两件事在旧实现里是被同一个字段混在一起的。
            Assert.IsTrue(target.Value > before, "the animation must advance");
        }
        finally
        {
            TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    [TestMethod]
    public void ANegativeTimeScaleIsRejectedRatherThanClamped()
    {
        var channel = Channel("negative-rate");
        TickManager.Start(channel);

        // 旧实现把 [0,10] 之外的值静默丢掉；现在逐字转发给总线，负值按总线的规矩抛。
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TickManager.SetTimeScale(-1f, channel));
    }

    [TestMethod]
    public async Task FixedUpdatePushesTrackTheVirtualClockNotTheWakeCadence()
    {
        var channel = Channel("fixed-exact");
        TickManager.Start(channel);

        var counter = new PushCounter();
        TickManager.RegisterBehaviour(counter, channel);

        try
        {
            Assert.IsTrue(await WaitUntilAsync(() => counter.Updates > 2, 3000), "the behaviour must be picked up");

            // 4 倍速 + 16ms 步长 = 每真实毫秒欠 0.25 步。旧实现每次醒来最多推一步，
            // 于是无论虚拟时钟走多快都只能推出「每真实 16ms 一步」；采样器欠多少就还多少。
            TickManager.SetTimeScale(4f, channel);

            var startFixed = counter.FixedUpdates;
            var started = Environment.TickCount64;
            await Task.Delay(600);
            var elapsed = Environment.TickCount64 - started;

            var pushed = counter.FixedUpdates - startFixed;
            var owed = 4d * elapsed / 16d; // rate * 真实时间 / 步长

            Assert.IsTrue(pushed >= owed * 0.7,
                $"pushes must track the virtual clock, expected about {owed:F0} but got {pushed}");
            Assert.IsTrue(pushed <= owed * 1.2, $"and must not push ahead of it, expected about {owed:F0} but got {pushed}");
        }
        finally
        {
            TickManager.UnregisterBehaviour(counter, channel);
        }
    }

    [TestMethod]
    public async Task ChangingTheFixedIntervalReachesTheSampler()
    {
        var channel = Channel("fixed-interval");
        TickManager.Start(channel);

        var counter = new PushCounter();
        TickManager.RegisterBehaviour(counter, channel);

        try
        {
            Assert.IsTrue(await WaitUntilAsync(() => counter.Updates > 2, 3000), "the behaviour must be picked up");

            // 步长是从另一个线程交过去的，只能由 fixed 循环自己在它自己的线程上落到采样器里。
            // 落不到的话，这里仍然会是默认 16ms 的节奏（约 44 步），而不是 200ms 的（约 3 步）。
            TickManager.SetFixedUpdateInterval(200, channel);

            var startFixed = counter.FixedUpdates;
            await Task.Delay(700);
            var pushed = counter.FixedUpdates - startFixed;

            // 两个界都要：上界排除「还是按 16ms 推」，下界排除「根本没在推」——后者也能满足上界。
            Assert.IsTrue(pushed is >= 2 and <= 8,
                $"a 200ms step over 700ms owes about 3 pushes, got {pushed}");
        }
        finally
        {
            TickManager.UnregisterBehaviour(counter, channel);
        }
    }

    [TestMethod]
    public async Task StoppingWhilePausedEndsTheChannel()
    {
        var channel = Channel("stop-while-paused");
        TickManager.Start(channel);

        Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > 1, 3000));

        // 停摆中的循环 park 在一个对令牌一无所知的等待上。不在这个等待里观察令牌的话，
        // StopAsync 会一直等下去，然后 ForceCleanup 释放掉那个令牌源，异常从线程体里逃出去。
        TickManager.Pause(channel);

        var stopping = TickManager.StopAsync(channel);
        var winner = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.AreSame(stopping, winner, "StopAsync must complete against a parked channel");
        Assert.IsFalse(TickManager.IsRunning(channel));
        Assert.IsFalse(TickManager.IsUpdateThreadAlive(channel));
        Assert.IsFalse(TickManager.IsFixedUpdateThreadAlive(channel));
    }

    [TestMethod]
    public async Task StartingAChannelClearsAPauseLeftOverFromTheLastLifecycle()
    {
        var channel = Channel("pause-across-restart");
        TickManager.Start(channel);

        Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > 1, 3000));

        // 暂停中停掉，再启动：暂停状态属于上一个生命周期，不能带过去。总线是渠道长期持有的对象，
        // 所以「停/启顺手清掉暂停」这件事必须显式做——旧实现靠 _isPaused = false 顺手做到了。
        TickManager.Pause(channel);
        await TickManager.StopAsync(channel);
        Assert.IsFalse(TickManager.IsPaused(channel), "a stop must not leave the channel paused");

        TickManager.Start(channel);
        try
        {
            Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > 0, 3000),
                "a restarted channel must pump, not park on a pause from the previous lifecycle");
        }
        finally
        {
            if (TickManager.IsRunning(channel)) await TickManager.StopAsync(channel);
        }
    }

    [TestMethod]
    public async Task RestartingAChannelRePrimesItsClocks()
    {
        var channel = Channel("restart");
        TickManager.Start(channel);

        Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > 1, 3000));

        TickManager.SetTimeScale(1f, channel);
        await Task.Delay(80);
        var beforeRestart = TickManager.TotalTime(channel);
        Assert.IsTrue(beforeRestart > TimeSpan.Zero);

        await TickManager.RestartAsync(channel);

        // 采样器在 Start 里重新锚定，所以重启后的第一帧不会带上整个停机时间——
        // 旧实现靠 _lastFrameTimestamp = GetTimestamp() 做同一件事。
        await Task.Delay(60);
        var afterRestart = TickManager.TotalTime(channel);
        Assert.IsTrue(afterRestart < beforeRestart,
            $"the total must restart from the new anchor, was {beforeRestart} now {afterRestart}");
    }

    [TestMethod]
    public async Task TheAsyncLoopPathDrivesTheSameBus()
    {
        var channel = Channel("async-path");
        TickManager.SetUseAsyncLoop(true, channel);
        TickManager.Start(channel);

        var bus = TickManager.Bus(channel);
        Assert.IsNotNull(bus);

        // WASM 那条路（async/await 而不是线程）必须走同一条时间轴，否则「一次 Pause 停两半」在上面不成立。
        Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > 2, 3000));

        TickManager.Pause(channel);
        await Task.Delay(60);
        var whilePaused = TickManager.TotalFrames(channel);
        await Task.Delay(150);

        Assert.AreEqual(whilePaused, TickManager.TotalFrames(channel), "the async path must park too");

        TickManager.Resume(channel);
        Assert.IsTrue(await WaitUntilAsync(() => TickManager.TotalFrames(channel) > whilePaused, 3000));
    }
}
