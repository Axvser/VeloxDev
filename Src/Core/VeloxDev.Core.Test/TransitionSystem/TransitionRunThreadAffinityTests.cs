using System.Linq.Expressions;
using VeloxDev.Threading;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// A run posts its frames to the thread it was started on, even when the host's answer depends on who is asking.
/// </summary>
/// <remarks>
/// Razor is the host this models. A Blazor Server process serves many circuits, each with its own
/// <see cref="SynchronizationContext"/>, so "the UI thread" is whichever circuit is asking — and the write path runs
/// on the sampling loop's thread, where that question has no answer at all. The answer therefore has to be taken when
/// the run starts, on the circuit's own thread, and pinned to that run.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class TransitionRunThreadAffinityTests
{
    private sealed class Target
    {
        public double Value { get; set; }
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    /// <summary>One circuit's renderer: counts what was handed to it.</summary>
    private sealed class Circuit(string name) : SynchronizationContext
    {
        public string Name { get; } = name;

        public int Posted { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            Posted++;
            d(state);
        }
    }

    private sealed class CircuitHost : TransitionHostBase<NonPriority>
    {
        [ThreadStatic] private static SynchronizationContext? _calling;

        private static SynchronizationContext? _captured;

        public static void Enter(SynchronizationContext? context) => _calling = context;

        public static void Reset()
        {
            _calling = null;
            _captured = null;
        }

        /// <summary>Razor's shape: the caller's circuit when there is one, the first circuit seen otherwise.</summary>
        public override ThreadRef ThreadFor(object target)
        {
            var current = _calling;
            _captured ??= current;
            return ThreadRef.From(current ?? _captured);
        }

        /// <summary>Never the caller's thread — the sampling loop runs off the circuit, which is the whole problem.</summary>
        protected override bool IsCurrentThread(ThreadRef thread) => false;

        protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority)
        {
            if (!thread.TryGet<SynchronizationContext>(out var context)) return false;

            context.Post(_ => action(), null);
            return true;
        }
    }

    private sealed class TestTransition : TransitionCore<
        Target, StateCore, TransitionEffectCore, TestInterpolator, CircuitHost, TestInterpreter, NonPriority>
    {
        public static TestTransition Create() => TransitionCore.Create<TestTransition>();

        public TestTransition Property<TValue>(Expression<Func<Target, TValue>> lambda, TValue value)
        {
            state.SetValue(lambda, value);
            return this;
        }

        public TestTransition Effect(TransitionEffectCore effect)
        {
            this.effect = effect;
            return this;
        }
    }

    [TestMethod]
    public void EachRunPostsToTheCircuitItWasStartedOn()
    {
        CircuitHost.Reset();

        var circuitA = new Circuit("A");
        var circuitB = new Circuit("B");
        var completed = 0;

        StartOn(circuitA, new Target(), () => Interlocked.Increment(ref completed));
        StartOn(circuitB, new Target(), () => Interlocked.Increment(ref completed));

        // 必须等两条都跑完，而不是等它们各收到一次投递：第一帧还在启动线程上，谁都答得对，
        // 问题从第二帧起、在池线程上才露头。
        var deadline = Environment.TickCount64 + 8000;
        while (Volatile.Read(ref completed) < 2 && Environment.TickCount64 < deadline) Thread.Sleep(20);

        Assert.AreEqual(2, Volatile.Read(ref completed), "两条动画都该跑完，否则下面的计数比不出东西");
        Assert.IsTrue(circuitB.Posted >= 2,
            $"电路 B 应当收到它自己那一趟的读取与帧，实际只收到 {circuitB.Posted}");
        Assert.AreEqual(circuitA.Posted, circuitB.Posted,
            "两趟动画形状相同，各自收到的投递数也该相同——不等就说明有一趟的帧落到了别人的电路上");
    }

    private static void StartOn(Circuit circuit, Target target, Action onCompleted)
    {
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(circuit);
            CircuitHost.Enter(circuit);

            var effect = new TransitionEffectCore { Duration = TimeSpan.FromMilliseconds(60), FPS = 120 };
            effect.Completed += (_, _) => onCompleted();

            TestTransition.Create()
                .Property(t => t.Value, 100d)
                .Effect(effect)
                .Execute(target);
        });

        thread.IsBackground = true;
        thread.Start();
        thread.Join();
    }
}
