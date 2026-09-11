using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// WorkflowSystem composite types (Offset/Anchor/Size) are animated member by member: each leaf is declared as an
/// explicit path, and Prepare interpolates it with its own sampler (generic double/int samplers). The composite
/// classes are reference types and do not implement ISampleable — only the Viewport struct does, and that is
/// assembled through StructAssembler. Whole-value `Property(x => x.Offset, end)` does not apply — composites are
/// always animated per member.
/// </summary>
[TestClass]
public class ISampleableAnimationTests
{
    private sealed class Target
    {
        public Offset Offset { get; set; } = new();
        public Anchor Anchor { get; set; } = new();
        public Size Size { get; set; } = new();
    }

    private sealed class TestInterpolator : InterpolatorCore { }

    private sealed class ImmediateInspector : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => true;
        public override bool IsUIThread() => true;
        public override object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
        public override void ProtectedInvoke(object target, Action action) => action();
    }

    private static readonly Func<Type, bool> CanAnimate = static type =>
        type == typeof(double) || type == typeof(int);

    [TestMethod]
    public void PrepareAndApply_AnimatesExpandedMembers()
    {
        var target = new Target { Offset = new Offset(0, 0), Anchor = new Anchor(0, 0, 0), Size = new Size(0, 0) };
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Offset.Horizontal, 10d);
        state.SetValue<Target, double>(t => t.Offset.Vertical, 20d);
        state.SetValue<Target, double>(t => t.Anchor.Horizontal, 10d);
        state.SetValue<Target, double>(t => t.Anchor.Vertical, 20d);
        state.SetValue<Target, int>(t => t.Anchor.Layer, 10);
        state.SetValue<Target, double>(t => t.Size.Width, 30d);
        state.SetValue<Target, double>(t => t.Size.Height, 40d);

        var frameSet = new TestInterpolator().Prepare(target, state, new TransitionEffectCore(), new ImmediateInspector());
        frameSet.Apply(target, 0.5);

        Assert.AreEqual(5d, target.Offset.Horizontal);
        Assert.AreEqual(10d, target.Offset.Vertical);
        Assert.AreEqual(5d, target.Anchor.Horizontal);
        Assert.AreEqual(10d, target.Anchor.Vertical);
        Assert.AreEqual(5, target.Anchor.Layer);
        Assert.AreEqual(15d, target.Size.Width);
        Assert.AreEqual(20d, target.Size.Height);
    }

    [TestMethod]
    public void WholeValue_SetValue_IsNotAnimated()
    {
        var target = new Target { Offset = new Offset(0, 0) };
        var state = new StateCore();
        state.SetValue<Target, Offset>(t => t.Offset, new Offset(10, 20)); // Whole value → not expanded, should be skipped

        var frameSet = new TestInterpolator().Prepare(target, state, new TransitionEffectCore(), new ImmediateInspector());
        frameSet.Apply(target, 0.5);

        Assert.AreEqual(0d, target.Offset.Horizontal);
        Assert.AreEqual(0d, target.Offset.Vertical);
    }
}
