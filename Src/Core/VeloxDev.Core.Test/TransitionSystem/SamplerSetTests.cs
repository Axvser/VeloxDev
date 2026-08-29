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

    private sealed class FakeInspector : IUIThreadInspectorCore
    {
        public Func<bool> Alive { get; set; } = static () => true;
        public int InvokeCount { get; private set; }
        public bool IsAppAlive() => Alive();
        public bool IsUIThread() => true;
        public void ProtectedInvoke(object target, Action action, object? priority = default) { InvokeCount++; action(); }
        public object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
    }

    private static ITransitionProperty Property => TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);

    [TestMethod]
    public void Apply_AppliesAllSamplers()
    {
        var inspector = new FakeInspector();
        var set = new SamplerSet(inspector);
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
        var set = new SamplerSet(inspector);
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
        var set = new SamplerSet(inspector);
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
        var set = new SamplerSet(inspector);
        set.Add(Property, new DoubleSampler(), 10d, 100d, null);
        var target = new Target();
        set.Apply(target, 0.5);
        Assert.AreEqual(0d, target.Value);
        Assert.AreEqual(0, inspector.InvokeCount);
    }
}
