using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// A declared path whose leaf is a reference type with no sampler animates nothing — a reference type is never
/// assembled from its members the way a struct is. It is rejected when the transition runs, instead of silently
/// doing nothing.
/// </summary>
[TestClass]
public class TransitionPathValidationTests
{
    private sealed class Target
    {
        public double Value { get; set; }
        public Leaf Child { get; set; } = new();
    }

    private sealed class Leaf
    {
        public double Width { get; set; }
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    private sealed class ImmediateInspector : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => true;
        public override bool IsUIThread() => true;
        public override object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
        public override bool ProtectedInvoke(object target, Action action) { action(); return true; }
    }

    private sealed class TestTransition : TransitionCore<
        Target,
        StateCore,
        TransitionEffectCore,
        TestInterpolator,
        ImmediateInspector,
        TestInterpreter,
        NonPriority>
    {
    }

    [TestMethod]
    public void Execute_WholeReferenceValuePath_Throws()
    {
        var transition = TransitionCore.Create<TestTransition>();
        // Leaf is a reference type and nothing samples it, so no frame would ever be written.
        transition.GetState().SetValue<Target, Leaf>(t => t.Child, new Leaf());

        var exception = Assert.Throws<TransitionPathUnsampleableException>(() => transition.Execute(new Target()));

        Assert.AreEqual("Child", exception.Property.Path);
    }

    [TestMethod]
    public void Execute_RegisteredLeafType_DoesNotThrow()
    {
        var target = new Target();
        var transition = TransitionCore.Create<TestTransition>();
        transition.GetState().SetValue<Target, double>(t => t.Value, 5d);

        transition.Execute(target);
        TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
    }

    [TestMethod]
    public void Execute_SubLeafOfAReferenceType_DoesNotThrow()
    {
        // The leaf is what has to be sampleable; navigating through a reference type on the way to it is fine.
        var target = new Target();
        var transition = TransitionCore.Create<TestTransition>();
        transition.GetState().SetValue<Target, double>(t => t.Child.Width, 5d);

        transition.Execute(target);
        TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
    }
}
