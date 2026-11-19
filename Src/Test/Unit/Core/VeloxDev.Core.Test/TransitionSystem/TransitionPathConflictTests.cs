using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// One object must be expressed by exactly one path. With a whole-object path and a sub-leaf path under it both
/// present, a whole-object sampler and a sub-leaf sampler would write the same object every frame and the outcome
/// would depend on the order they happen to run in — so the conflict is rejected rather than resolved silently.
/// </summary>
[TestClass]
public class TransitionPathConflictTests
{
    private sealed class Leaf
    {
        public double Width { get; set; }
    }

    private sealed class Node
    {
        public Leaf Child { get; set; } = new();
        public double Value { get; set; }
    }

    [TestMethod]
    public void SetValue_ChildAfterParent_Throws()
    {
        var state = new StateCore();
        state.SetValue<Node, Leaf>(n => n.Child, new Leaf());

        var exception = Assert.Throws<TransitionPathConflictException>(
            () => state.SetValue<Node, double>(n => n.Child.Width, 5d));

        Assert.AreEqual("Child", exception.Existing.Path);
        Assert.AreEqual("Child.Width", exception.Conflicting.Path);
    }

    [TestMethod]
    public void SetValue_ParentAfterChild_Throws()
    {
        var state = new StateCore();
        state.SetValue<Node, double>(n => n.Child.Width, 5d);

        var exception = Assert.Throws<TransitionPathConflictException>(
            () => state.SetValue<Node, Leaf>(n => n.Child, new Leaf()));

        Assert.AreEqual("Child.Width", exception.Existing.Path);
        Assert.AreEqual("Child", exception.Conflicting.Path);
    }

    [TestMethod]
    public void SetValue_SamePathTwice_OverwritesInsteadOfThrowing()
    {
        var state = new StateCore();
        state.SetValue<Node, double>(n => n.Value, 1d);
        state.SetValue<Node, double>(n => n.Value, 2d);

        Assert.AreEqual(1, state.Values.Count);
        Assert.IsTrue(state.TryGetValue<Node, double>(n => n.Value, out var value));
        Assert.AreEqual(2d, value);
    }

    [TestMethod]
    public void SetValue_UnrelatedPaths_DoNotThrow()
    {
        var state = new StateCore();
        state.SetValue<Node, double>(n => n.Value, 1d);
        state.SetValue<Node, double>(n => n.Child.Width, 5d);

        Assert.AreEqual(2, state.Values.Count);
    }
}
