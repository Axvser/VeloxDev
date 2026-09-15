using System.Linq.Expressions;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// What a run reports when it degrades or fails, and the boundary between that and a definition-phase refusal.
/// </summary>
/// <remarks>
/// An animation never propagates an exception and never ends its host: every stage below ends the run, or carries on
/// past it, with the report going to the effect's Debug line and <c>Warn</c>/<c>Error</c> events.
/// </remarks>
[TestClass]
public class TransitionDiagnosticsTests
{
    private sealed class Alpha
    {
        public double X { get; set; }
    }

    private sealed class Beta
    {
        public double X { get; set; }
    }

    private sealed class Target
    {
        public double Value { get; set; }

        public object Shape { get; set; } = new Alpha();
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    private sealed class ThrowingSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
            => throw new InvalidOperationException("sampler");
    }

    private sealed class TestTransition : TransitionCore<
        Target, StateCore, TransitionEffectCore, TestInterpolator, ImmediateHost, TestInterpreter, NonPriority>
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

    private static ITransitionProperty ValuePath
        => TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);

    [TestMethod]
    public async Task AThrowingUpdateEndsTheRunAndIsReported()
    {
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 100d);

        var effect = new TransitionEffectCore { Duration = TimeSpan.FromMilliseconds(200), FPS = 60 };
        effect.Update += (_, _) => throw new InvalidOperationException("callback");

        var reports = new List<TransitionEventArgs>();
        effect.Error += (_, e) => reports.Add(e);

        var target = new Target();
        var set = new TestInterpolator().Prepare(target, state, effect, new ImmediateHost());
        using var cts = new CancellationTokenSource();

        // 不抛：动画自己结束。
        await new TestInterpreter().Execute(target, set, effect, cts);

        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual("Update", reports[0].Stage);
        Assert.IsInstanceOfType<InvalidOperationException>(reports[0].Exception);
    }

    [TestMethod]
    public void AThrowingSamplerIsReportedAndStopsTheFrames()
    {
        var target = new Target();
        var effect = new TransitionEffectCore();
        var reports = new List<TransitionEventArgs>();
        effect.Error += (_, e) => reports.Add(e);

        using var cts = new CancellationTokenSource();
        var set = new SamplerSet<NonPriority>(new ImmediateHost());
        set.Add(ValuePath, new ThrowingSampler(), 10d, 100d, null);
        set.SetCancellation(cts);
        set.SetDiagnostics(new TransitionDiagnostics(effect, target, new TransitionEventArgs()));

        set.Apply(target, 0.5);

        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual("Sampling", reports[0].Stage);
        Assert.IsTrue(cts.IsCancellationRequested, "a run that cannot draw must stop rather than throw once per frame");
    }

    [TestMethod]
    public async Task AnUnreadablePathIsWarnedAndTheRestStillAnimates()
    {
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 100d);
        // Shape 静态类型是 object，这条路径声明在 Beta 上，而目标持有的是 Alpha。
        state.SetValue<Target, double>(t => ((Beta)t.Shape).X, 5d);

        var effect = new TransitionEffectCore { Duration = TimeSpan.Zero };
        var warnings = new List<TransitionEventArgs>();
        effect.Warn += (_, e) => warnings.Add(e);

        var target = new Target();
        var set = new TestInterpolator().Prepare(target, state, effect, new ImmediateHost());
        using var cts = new CancellationTokenSource();

        await new TestInterpreter().Execute(target, set, effect, cts);

        Assert.AreEqual(1, warnings.Count);
        Assert.AreEqual("Unreadable", warnings[0].Stage);
        Assert.AreEqual(100d, target.Value, "the property that does match the target must still be animated");
    }

    [TestMethod]
    public void AWarnHandlerCanEndTheRun()
    {
        var target = new Target();
        var effect = new TransitionEffectCore();
        var runArgs = new TransitionEventArgs();
        effect.Warn += (_, e) => e.Handled = true;

        // 一个拒绝排队的宿主：帧发不出去，Warn 把这一趟结束掉。
        var set = new SamplerSet<NonPriority>(new RefusingHost());
        set.Add(ValuePath, new DoubleSampler(), 10d, 100d, null);
        set.SetDiagnostics(new TransitionDiagnostics(effect, target, runArgs));

        set.Apply(target, 0.5);

        Assert.IsTrue(runArgs.Handled, "a handler that takes responsibility for a Warn ends the run");
    }

    [TestMethod]
    public void AnUnsampleablePathStillThrowsToTheCallerAndIsNotReported()
    {
        var effect = new TransitionEffectCore { Duration = TimeSpan.Zero };
        var errors = 0;
        effect.Error += (_, _) => errors++;

        var transition = TestTransition.Create()
            .Effect(effect)
            .Property(t => t.Shape, new object());

        // 定义期的拒绝走调用者，不进运行期的报错通道。
        Assert.ThrowsExactly<TransitionPathUnsampleableException>(() => transition.Execute(new Target()));
        Assert.AreEqual(0, errors);
    }

    private sealed class RefusingHost : TransitionHostBase<NonPriority>
    {
        public override ThreadRef ThreadFor(object target) => ThreadRef.None;

        protected override bool IsCurrentThread(ThreadRef thread) => false;

        protected override bool PostCore(object target, Action action, NonPriority priority) => false;
    }
}
