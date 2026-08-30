using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// Snapshot discovery records sub-leaves recursively (cycle-guarded): registered types are captured as whole paths;
/// ISampleable values expand their declared members into member paths (one level); plain non-ISampleable composites
/// are descended into so the snapshot records their sub-leaf values.
/// </summary>
[TestClass]
public class TransitionSnapshotHelperTests
{
    // Registry-driven predicate: only double is animatable
    private static readonly Func<Type, bool> CanAnimate = static type => type == typeof(double);

    private sealed class MetaType : ISampleable
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public string Name { get; set; } = "";

        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<MetaType>(t => t.Width, t => t.Height);
    }

    private sealed class ComplexChild
    {
        public double X { get; set; }
    }

    private sealed class Outer
    {
        public double Direct { get; set; }
        public MetaType Meta { get; set; } = new();
        public ComplexChild Child { get; set; } = new(); // Not ISampleable → not descended
    }

    [TestMethod]
    public void Discover_RegisteredType_ProducesSingleSegmentPath()
    {
        var properties = TransitionSnapshotHelper.DiscoverAnimatableProperties(new Outer(), CanAnimate);

        Assert.IsTrue(properties.Any(p => p.Path == "Direct"));
        Assert.IsTrue(properties.All(p => p.Path != "Meta")); // The composite value itself is not directly animatable
    }

    [TestMethod]
    public void Discover_ExpandsDeclaredMembers_IntoMemberPaths()
    {
        var paths = TransitionSnapshotHelper
            .DiscoverAnimatableProperties(new Outer(), CanAnimate)
            .Select(p => p.Path)
            .ToHashSet();

        Assert.IsTrue(paths.Contains("Direct"));      // Directly animatable
        Assert.IsTrue(paths.Contains("Meta.Width"));  // ISampleable declared member expansion
        Assert.IsTrue(paths.Contains("Meta.Height"));
        Assert.IsFalse(paths.Contains("Meta.Name"));  // string has no sampler → skipped
        Assert.IsTrue(paths.Contains("Child.X"));     // Plain composite → recursed into, sub-leaf recorded
    }

    [TestMethod]
    public void CaptureAll_CapturesExpandedMemberValues()
    {
        var target = new Outer
        {
            Direct = 42,
            Meta = new MetaType { Width = 5, Height = 10 },
        };
        var state = new StateCore();

        TransitionSnapshotHelper.CaptureAll(target, state, CanAnimate);

        Assert.IsTrue(state.TryGetValue<Outer, double>(o => o.Direct, out var direct));
        Assert.AreEqual(42d, direct);
        Assert.IsTrue(state.TryGetValue<Outer, double>(o => o.Meta.Width, out var width));
        Assert.AreEqual(5d, width);
        Assert.IsTrue(state.TryGetValue<Outer, double>(o => o.Meta.Height, out var height));
        Assert.AreEqual(10d, height);
        Assert.IsTrue(state.TryGetValue<Outer, double>(o => o.Child.X, out _)); // Sub-leaf recorded via recursion
    }

    [TestMethod]
    public void CaptureSpecific_OnlyCapturesExplicitExpressions()
    {
        var target = new Outer { Direct = 42, Meta = new MetaType { Width = 5 } };
        var state = new StateCore();

        TransitionSnapshotHelper.CaptureSpecific(target, state, [o => o.Meta.Width]);

        Assert.IsFalse(state.TryGetValue<Outer, double>(o => o.Direct, out _));
        Assert.IsTrue(state.TryGetValue<Outer, double>(o => o.Meta.Width, out var width));
        Assert.AreEqual(5d, width);
    }
}
