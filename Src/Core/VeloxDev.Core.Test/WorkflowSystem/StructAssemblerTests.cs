using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Struct ISampleable assembly: a value type like <see cref="Viewport"/> is captured as a whole value and
/// interpolated by reconstructing it through its constructor from the interpolated members (member paths cannot be
/// written back through a value type).
/// </summary>
[TestClass]
public class StructAssemblerTests
{
    private sealed class Target
    {
        public Viewport Viewport { get; set; }
    }

    private sealed class TestInterpolator : InterpolatorCore { }

    private sealed class ImmediateInspector : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => true;
        public override bool IsUIThread() => true;
        public override object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
        public override void ProtectedInvoke(object target, Action action) => action();
    }

    private static readonly Func<Type, bool> CanAnimate = static type => type == typeof(double);

    [TestMethod]
    public void CaptureAll_StoresWholeStruct_NotMemberPaths()
    {
        var target = new Target { Viewport = new Viewport(0, 0, 10, 10) };
        var state = new StateCore();

        TransitionSnapshotHelper.CaptureAll(target, state, CanAnimate);

        var paths = state.Values.Keys.Select(p => p.Path).ToHashSet();
        Assert.IsTrue(paths.Contains("Viewport"));                // whole struct path
        Assert.IsFalse(paths.Contains("Viewport.Horizontal"));    // no member expansion for structs
    }

    [TestMethod]
    public void PrepareAndApply_InterpolatesWholeStruct()
    {
        var target = new Target { Viewport = new Viewport(0, 0, 0, 0) };
        var state = new StateCore();
        state.SetValue<Target, Viewport>(t => t.Viewport, new Viewport(10, 20, 30, 40));

        var frameSet = new TestInterpolator().Prepare(target, state, new TransitionEffectCore(), new ImmediateInspector());
        frameSet.Apply(target, 0.5);

        Assert.AreEqual(5d, target.Viewport.Horizontal);
        Assert.AreEqual(10d, target.Viewport.Vertical);
        Assert.AreEqual(15d, target.Viewport.Width);
        Assert.AreEqual(20d, target.Viewport.Height);
    }

    [TestMethod]
    public void PrepareAndApply_Endpoint_IsExactEnd()
    {
        var target = new Target { Viewport = new Viewport(0, 0, 0, 0) };
        var state = new StateCore();
        state.SetValue<Target, Viewport>(t => t.Viewport, new Viewport(10, 20, 30, 40));

        var frameSet = new TestInterpolator().Prepare(target, state, new TransitionEffectCore(), new ImmediateInspector());
        frameSet.Apply(target, 1.0);

        Assert.AreEqual(10d, target.Viewport.Horizontal);
        Assert.AreEqual(20d, target.Viewport.Vertical);
        Assert.AreEqual(30d, target.Viewport.Width);
        Assert.AreEqual(40d, target.Viewport.Height);
    }
}
