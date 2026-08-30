using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// WorkflowSystem composite types (Offset/Anchor/Size) animate via ISampleable metadata member decomposition:
/// capture expands the property into member paths, and Prepare interpolates each member by its leaf sampler
/// (generic double/int samplers). Whole-value `Property(x => x.Offset, end)` does not apply — composites are
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
    public void CaptureAll_ExpandsWorkflowTypes_IntoMemberPaths()
    {
        var target = new Target();
        var state = new StateCore();

        TransitionSnapshotHelper.CaptureAll(target, state, CanAnimate);

        var paths = state.Values.Keys.Select(p => p.Path).ToHashSet();
        Assert.IsTrue(paths.Contains("Offset.Horizontal"));
        Assert.IsTrue(paths.Contains("Offset.Vertical"));
        Assert.IsTrue(paths.Contains("Anchor.Horizontal"));
        Assert.IsTrue(paths.Contains("Anchor.Vertical"));
        Assert.IsTrue(paths.Contains("Anchor.Layer"));
        Assert.IsTrue(paths.Contains("Size.Width"));
        Assert.IsTrue(paths.Contains("Size.Height"));
        // Composite types are not captured as whole values
        Assert.IsFalse(paths.Contains("Offset"));
        Assert.IsFalse(paths.Contains("Anchor"));
        Assert.IsFalse(paths.Contains("Size"));
    }

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
