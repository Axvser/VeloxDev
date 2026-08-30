using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// Snapshot discovery records sub-leaves recursively (cycle-guarded): registered types are captured as whole paths;
/// ISampleable is a green light — its declared members expand all the way down (nested ISampleable members too);
/// plain non-ISampleable composites are descended into so the snapshot records their sub-leaf values.
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

        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
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

    private sealed class Inner : ISampleable
    {
        public double X { get; set; }
        public double Y { get; set; }
        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<Inner>(i => i.X, i => i.Y);
        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
    }

    private sealed class NestedMeta : ISampleable
    {
        public double Direct { get; set; }
        public Inner Nested { get; set; } = new();
        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<NestedMeta>(n => n.Direct, n => n.Nested);
        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
    }

    private sealed class SelfReferencingNode : ISampleable
    {
        public double Value { get; set; }
        public SelfReferencingNode? Next { get; set; }
        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<SelfReferencingNode>(n => n.Value, n => n.Next);
        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
    }

    private sealed class NodeHolder
    {
        public SelfReferencingNode Node { get; set; } = new();
    }

    private sealed class TypeCycleA : ISampleable
    {
        public TypeCycleB? B { get; set; }
        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<TypeCycleA>(a => a.B);
        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
    }

    private sealed class TypeCycleB : ISampleable
    {
        public double X { get; set; }
        public TypeCycleA? A { get; set; }
        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<TypeCycleB>(b => b.X, b => b.A);
        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
    }

    private sealed class TypeCycleHolder { public TypeCycleA A { get; set; } = new(); }

    private sealed class ObjectCycleA : ISampleable
    {
        public ObjectCycleB B { get; set; } = new();
        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<ObjectCycleA>(a => a.B);
        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
    }

    private sealed class ObjectCycleB : ISampleable
    {
        public double X { get; set; }
        public ObjectCycleA? Back { get; set; }
        public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
            TransitionProperty.Members<ObjectCycleB>(b => b.X, b => b.Back);
        public object? CreateFrameValue(IReadOnlyList<object?> memberValues) => null;
    }

    private sealed class ObjectCycleHolder { public ObjectCycleA A { get; set; } = new(); }

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
    public void Discover_ExpandsNestedSampleableMembers_Recursively()
    {
        // A declared member that is itself ISampleable expands all the way down (green light).
        var paths = TransitionSnapshotHelper
            .DiscoverAnimatableProperties(new NestedMeta(), CanAnimate)
            .Select(p => p.Path)
            .ToHashSet();

        Assert.IsTrue(paths.Contains("Direct"));    // registered leaf
        Assert.IsTrue(paths.Contains("Nested.X"));  // nested ISampleable expanded all the way
        Assert.IsTrue(paths.Contains("Nested.Y"));
        Assert.IsFalse(paths.Contains("Nested"));   // the composite itself is not a leaf
    }

    [TestMethod]
    public void Discover_SelfReferencingSampleable_StopsAtSelfReference()
    {
        // The ancestor-type guard bounds a self-referencing ISampleable (no maxDepth magic number): a member whose
        // type is already on the current path stops the recursion.
        var node = new SelfReferencingNode { Value = 1, Next = new SelfReferencingNode { Value = 2 } };
        node.Next!.Next = node; // cycle

        var paths = TransitionSnapshotHelper
            .DiscoverAnimatableProperties(new NodeHolder { Node = node }, CanAnimate)
            .Select(p => p.Path)
            .ToHashSet();

        Assert.IsTrue(paths.Contains("Node.Value"));       // root ISampleable expands once
        Assert.IsFalse(paths.Contains("Node.Next.Value")); // same type already on path → stop
        Assert.IsFalse(paths.Contains("Node.Next.Next.Value"));
    }

    [TestMethod]
    public void Discover_TypeCycle_StopsAtCycle()
    {
        // A ↔ B: expanding A → B → A' (a DIFFERENT instance of type A, already on the path) → the ancestor-type
        // guard stops it (the object guard does not fire here — A' is a distinct object).
        var a = new TypeCycleA();
        var b = new TypeCycleB { X = 5 };
        a.B = b;
        b.A = new TypeCycleA(); // different instance of type A → type guard fires
        var holder = new TypeCycleHolder { A = a };

        var paths = TransitionSnapshotHelper
            .DiscoverAnimatableProperties(holder, CanAnimate)
            .Select(p => p.Path)
            .ToHashSet();

        Assert.IsTrue(paths.Contains("A.B.X"));      // first level reaches the leaf
        Assert.IsFalse(paths.Contains("A.B.A.X"));   // type A already on path → cycle cut
        Assert.IsFalse(paths.Contains("A.B.A.B.X"));
    }

    [TestMethod]
    public void Discover_ObjectBackReference_StopsAtSameInstance()
    {
        // B.Back points back to the SAME A instance — the object-cycle guard (ancestors) stops it.
        var a = new ObjectCycleA();
        a.B.Back = a;
        var holder = new ObjectCycleHolder { A = a };

        var paths = TransitionSnapshotHelper
            .DiscoverAnimatableProperties(holder, CanAnimate)
            .Select(p => p.Path)
            .ToHashSet();

        Assert.IsTrue(paths.Contains("A.B.X"));        // leaf reachable
        Assert.IsFalse(paths.Contains("A.B.Back.X"));  // same instance already on path → cycle cut
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
