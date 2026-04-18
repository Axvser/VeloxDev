using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeInterpolators;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class StateCoreTests
{
    private sealed class Target
    {
        public double Value { get; set; }
        public string? Name { get; set; }
    }

    [TestMethod]
    public void SetValue_Expression_CanRetrieve()
    {
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 42.0);

        Assert.IsTrue(state.TryGetValue<Target, double>(t => t.Value, out var val));
        Assert.AreEqual(42.0, val);
    }

    [TestMethod]
    public void SetValue_PropertyInfo_CanRetrieve()
    {
        var state = new StateCore();
        var prop = TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);
        state.SetValue(prop, 99.0);

        Assert.IsTrue(state.TryGetValue(prop, out var val));
        Assert.AreEqual(99.0, val);
    }

    [TestMethod]
    public void TryGetValue_Missing_ReturnsFalse()
    {
        var state = new StateCore();
        Assert.IsFalse(state.TryGetValue<Target, double>(t => t.Value, out _));
    }

    [TestMethod]
    public void SetInterpolator_Expression_CanRetrieve()
    {
        var state = new StateCore();
        var interp = new DoubleInterpolator();
        state.SetInterpolator<Target, double>(t => t.Value, interp);

        Assert.IsTrue(state.TryGetInterpolator<Target, double>(t => t.Value, out var result));
        Assert.AreSame(interp, result);
    }

    [TestMethod]
    public void TryGetInterpolator_Missing_ReturnsFalse()
    {
        var state = new StateCore();
        Assert.IsFalse(state.TryGetInterpolator<Target, double>(t => t.Value, out _));
    }

    [TestMethod]
    public void SetValue_Overwrite_UpdatesValue()
    {
        var state = new StateCore();
        var prop = TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);
        state.SetValue(prop, 1.0);
        state.SetValue(prop, 2.0);

        Assert.IsTrue(state.TryGetValue(prop, out var val));
        Assert.AreEqual(2.0, val);
    }

    [TestMethod]
    public void Clone_ReturnsIndependentCopy()
    {
        var state = new StateCore();
        var prop = TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);
        state.SetValue(prop, 10.0);

        var clone = state.Clone();
        Assert.IsTrue(clone.TryGetValue(prop, out var val));
        Assert.AreEqual(10.0, val);

        // Mutating clone doesn't affect original
        clone.SetValue(prop, 99.0);
        Assert.IsTrue(state.TryGetValue(prop, out var origVal));
        Assert.AreEqual(10.0, origVal);
    }
}
