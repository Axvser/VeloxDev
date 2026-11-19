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

    private sealed class FakeInspector : InlinePostHost<NonPriority>
    {
        public Func<bool> Alive { get; set; } = static () => true;
        public int InvokeCount { get; private set; }
        public override bool IsAlive => Alive();
        protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority) { InvokeCount++; action(); return true; }
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


    [TestMethod]
    public void Apply_FrameQueuedBeforeCancellation_IsDroppedWhenItFinallyLands()
    {
        // A frame queued while the animation was still alive, cancelled before the UI thread pumped it, must be
        // dropped when it lands — otherwise it executes after a reset has already been applied and overwrites it.
        var inspector = new DeferredHost();
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


    [TestMethod]
    public void Apply_DoesNotAllocate_ForAValueTypePriority()
    {
        // The frame path runs once per frame per animation, so the priority must reach the inspector unboxed. It
        // used to travel as an object? parameter, which boxed a DispatcherPriority every frame; this is the only
        // thing that can prove the box is gone — reading the signature cannot.
        var set = new SamplerSet<FakePriority>(new InlinePostHost<FakePriority>());
        var target = new Target();

        set.Apply(target, 0.5, FakePriority.High); // warms up: caches the reusable apply delegate

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            set.Apply(target, 0.5, FakePriority.High);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(before, after, "the priority must reach the host unboxed");
    }
}
