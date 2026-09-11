using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class SamplerSetTests
{
    private sealed class Target
    {
        public double Value { get; set; }
    }

    private sealed class FakeInspector : IUIThreadInspector<NonPriority>
    {
        public Func<bool> Alive { get; set; } = static () => true;
        public int InvokeCount { get; private set; }
        public bool IsAppAlive() => Alive();
        public bool IsUIThread() => true;
        public void ProtectedInvoke(object target, Action action, object? priority = default) => ProtectedInvoke(target, action, default(NonPriority));
        public void ProtectedInvoke(object target, Action action, NonPriority priority) { InvokeCount++; action(); }
        public object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
    }

    private static ITransitionProperty Property => TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);

    [TestMethod]
    public void Apply_AppliesAllSamplers()
    {
        var inspector = new FakeInspector();
        var set = new SamplerSet<NonPriority>(inspector);
        set.Add(Property, new DoubleSampler(), 10d, 100d, null);
        var target = new Target();
        set.Apply(target, 0.5);
        Assert.AreEqual(55d, target.Value);
        Assert.AreEqual(1, inspector.InvokeCount);
    }

    [TestMethod]
    public void Apply_AtEndpoints_WritesExactValues()
    {
        var inspector = new FakeInspector();
        var set = new SamplerSet<NonPriority>(inspector);
        set.Add(Property, new DoubleSampler(), 10d, 100d, null);
        var target = new Target();
        set.Apply(target, 0.0);
        Assert.AreEqual(10d, target.Value);
        set.Apply(target, 1.0);
        Assert.AreEqual(100d, target.Value);
    }

    [TestMethod]
    public void Apply_AfterCancellation_SkipsWrites()
    {
        var inspector = new FakeInspector();
        var set = new SamplerSet<NonPriority>(inspector);
        set.Add(Property, new DoubleSampler(), 10d, 100d, null);
        var target = new Target { Value = 50d };
        using var cts = new CancellationTokenSource();
        set.SetCancellation(cts);
        cts.Cancel();
        set.Apply(target, 0.5);
        // stale queued frame skipped — reset result preserved
        Assert.AreEqual(50d, target.Value);
        Assert.AreEqual(0, inspector.InvokeCount);
    }

    [TestMethod]
    public void Apply_WhenAppDead_SkipsWrites()
    {
        var inspector = new FakeInspector { Alive = static () => false };
        var set = new SamplerSet<NonPriority>(inspector);
        set.Add(Property, new DoubleSampler(), 10d, 100d, null);
        var target = new Target();
        set.Apply(target, 0.5);
        Assert.AreEqual(0d, target.Value);
        Assert.AreEqual(0, inspector.InvokeCount);
    }

    /// <summary>
    /// Models the real marshalling: the write is queued and lands on the UI thread later, so the point where the
    /// frame is checked and the point where it is written are different moments.
    /// </summary>
    private sealed class DeferredInspector : IUIThreadInspector<NonPriority>
    {
        private readonly List<Action> _pending = [];

        public bool IsAppAlive() => true;

        public bool IsUIThread() => true;

        public void ProtectedInvoke(object target, Action action, object? priority = default) => _pending.Add(action);

        public void ProtectedInvoke(object target, Action action, NonPriority priority) => _pending.Add(action);

        public object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);

        public void Pump()
        {
            var pending = _pending.ToArray();
            _pending.Clear();
            foreach (var action in pending)
            {
                action();
            }
        }
    }

    [TestMethod]
    public void Apply_FrameQueuedBeforeCancellation_IsDroppedWhenItFinallyLands()
    {
        // A frame queued while the animation was still alive, cancelled before the UI thread pumped it, must be
        // dropped when it lands — otherwise it executes after a reset has already been applied and overwrites it.
        var inspector = new DeferredInspector();
        var set = new SamplerSet<NonPriority>(inspector);
        set.Add(Property, new DoubleSampler(), 10d, 100d, null);
        var target = new Target { Value = 50d };
        using var cts = new CancellationTokenSource();
        set.SetCancellation(cts);

        set.Apply(target, 1.0);   // queued while alive
        cts.Cancel();             // cancelled before the UI thread gets to it
        target.Value = 42d;       // the reset lands first
        inspector.Pump();         // ...then the stale frame runs

        Assert.AreEqual(42d, target.Value);
    }

    /// <summary>A stand-in for a host dispatcher priority, the way DispatcherPriority is one.</summary>
    private enum FakePriority
    {
        Low,
        High,
    }

    private sealed class PriorityInspector : IUIThreadInspector<FakePriority>
    {
        public bool IsAppAlive() => true;
        public bool IsUIThread() => true;
        public void ProtectedInvoke(object target, Action action, object? priority = default) => action();
        public void ProtectedInvoke(object target, Action action, FakePriority priority) => action();
        public object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
    }

    [TestMethod]
    public void Apply_DoesNotAllocate_ForAValueTypePriority()
    {
        // The frame path runs once per frame per animation, so the priority must reach the inspector unboxed. It
        // used to travel as an object? parameter, which boxed a DispatcherPriority every frame; this is the only
        // thing that can prove the box is gone — reading the signature cannot.
        var set = new SamplerSet<FakePriority>(new PriorityInspector());
        var target = new Target();

        set.Apply(target, 0.5, FakePriority.High); // warms up: caches the reusable apply delegate

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            set.Apply(target, 0.5, FakePriority.High);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(before, after, "Apply must not allocate per frame");
    }
}
