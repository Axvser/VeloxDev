using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// Awake has to have run before the frames are prepared: it can veto the animation through Args.Handled, and it may
/// put the target into the state the animation is meant to start from. On the five adapters that dispatch
/// fire-and-forget, an unawaited Awake ran after Prepare instead — which is what these two tests pin.
/// </summary>
[TestClass]
public class TransitionSchedulerAwakeTests
{
    private sealed class Target
    {
        public double Value { get; set; }
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    /// <summary>
    /// Models the five fire-and-forget adapters from the background-thread side: this is not the UI thread, and
    /// ProtectedInvoke queues the action and returns. Nothing runs until <see cref="Pump"/> stands in for the UI
    /// thread draining its queue.
    /// </summary>
    private sealed class OffThreadInspector : UIThreadInspectorCore
    {
        public static readonly List<Action> Pending = [];

        public override bool IsAppAlive() => true;
        public override bool IsUIThread() => false;
        public override object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
        public override bool ProtectedInvoke(object target, Action action) { Pending.Add(action); return true; }

        public static void Pump()
        {
            var pending = Pending.ToArray();
            Pending.Clear();
            foreach (var action in pending)
            {
                action();
            }
        }
    }

    /// <summary>Records the order in which the scheduler awakes the effect and prepares its frames.</summary>
    private sealed class RecordingInterpolator : InterpolatorCore
    {
        private readonly List<string> _order;

        public RecordingInterpolator(List<string> order) => _order = order;

        public override SamplerSet<TPriorityCore> Prepare<TPriorityCore>(object target, IFrameState state, ITransitionEffectCore effect, IUIThreadInspector<TPriorityCore> inspector)
        {
            _order.Add("prepare");
            return base.Prepare(target, state, effect, inspector);
        }
    }

    private static TransitionSchedulerCore<OffThreadInspector, TestInterpreter, NonPriority> SchedulerFor(Target target)
        => (TransitionSchedulerCore<OffThreadInspector, TestInterpreter, NonPriority>)
           TransitionSchedulerCore<OffThreadInspector, TestInterpreter, NonPriority>.FindOrCreate(target, CanMutualTask: false);

    private static StateCore StateFor()
    {
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 100d);
        return state;
    }

    [TestMethod]
    public async Task Execute_AwakesBeforePreparing()
    {
        OffThreadInspector.Pending.Clear();
        var order = new List<string>();
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.Zero };
        effect.Awaked += (_, _) => order.Add("awake");

        using var cts = new CancellationTokenSource();
        var running = SchedulerFor(target).Execute(new RecordingInterpolator(order), StateFor(), effect, cts);

        // Still parked on the Awake: nothing may have been prepared while it sits in the queue.
        Assert.IsEmpty(order, "Prepare must not run before the queued Awake has");

        OffThreadInspector.Pump();
        await running;

        Assert.AreEqual("awake,prepare", string.Join(",", order));
    }

    [TestMethod]
    public async Task Execute_AwakeHandledBeforeStart_VetoesTheAnimation()
    {
        OffThreadInspector.Pending.Clear();
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.Zero };

        int updates = 0;
        effect.Awaked += (_, args) => args.Handled = true;
        effect.Update += (_, _) => updates++;

        using var cts = new CancellationTokenSource();
        var running = SchedulerFor(target).Execute(new RecordingInterpolator([]), StateFor(), effect, cts);

        OffThreadInspector.Pump();
        await running;

        Assert.AreEqual(0, updates, "an Awake that vetoes the animation must be seen before the first frame");
    }
}
