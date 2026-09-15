using System.Linq.Expressions;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// 索引器路径（<c>x.Items[0].Width</c>、<c>x.Map["a"].Color</c>）。重点不在能不能解析，而在<b>身份</b>：
/// 路径是 <c>StateCore</c> 字典的键，而同一个 lambda 会被解析多次，所以这里的每一条断言都在守「两条不同的
/// 路径不能合成一个键、同一条路径不能裂成两个键」。
/// </summary>
[TestClass]
public class TransitionPropertyIndexerTests
{
    [TestMethod]
    public void ParsesAnIndexerSegment()
    {
        var property = Path(x => x.Items[0].Width);

        Assert.AreEqual("Items[0].Width", property.Path);
        Assert.AreEqual(typeof(double), property.PropertyType);
        Assert.IsTrue(property.CanRead);
        Assert.IsTrue(property.CanWrite);
    }

    [TestMethod]
    public void ParsesANonIntegerKey()
    {
        var property = Path(x => x.Map["player"].Width);

        Assert.AreEqual("Map[\"player\"].Width", property.Path);
        Assert.AreEqual(typeof(double), property.PropertyType);
    }

    [TestMethod]
    public void ParsesAnArrayElementAsTheLeaf()
    {
        var property = Path(x => x.Values[2]);

        Assert.AreEqual("Values[2]", property.Path);
        Assert.AreEqual(typeof(double), property.PropertyType);
    }

    [TestMethod]
    public void ParsesAMultiArgumentIndexer()
    {
        var property = Path(x => x.Cells[1, 2]);

        Assert.AreEqual("Cells[1, 2]", property.Path);
    }

    [TestMethod]
    public void ParsesAMultiDimensionalArray()
    {
        var property = Path(x => x.Grid[1, 1]);

        Assert.AreEqual("Grid[1, 1]", property.Path);
    }

    [TestMethod]
    public void ReadsAndWritesTheIndexedSlot()
    {
        var target = new Target();
        var property = Path(x => x.Items[1].Width);

        Assert.AreEqual(0d, property.GetValue(target));
        Assert.IsTrue(property.SetValue(target, 42d));
        Assert.AreEqual(42d, target.Items[1].Width);
        Assert.AreEqual(0d, target.Items[0].Width);
    }

    [TestMethod]
    public void WritesAnArrayElementInPlace()
    {
        var target = new Target();
        var property = Path(x => x.Values[1]);

        Assert.IsTrue(property.SetValue(target, 9d));
        Assert.AreEqual(9d, target.Values[1]);
    }

    [TestMethod]
    public void WritesThroughAMultiArgumentIndexer()
    {
        var target = new Target();
        var property = Path(x => x.Cells[1, 2]);

        Assert.IsTrue(property.SetValue(target, 5d));
        Assert.AreEqual(5d, target.Cells[1, 2]);
    }

    // ── 身份 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DifferentIndicesAreDifferentPaths()
    {
        // 索引器的 PropertyInfo 对每个下标都是同一个 "Item"：索引值不并入身份的话，这两个会合成一个字典键，
        // 于是两个动画互相静默覆盖。
        Assert.AreNotEqual(Path(x => x.Items[0].Width), Path(x => x.Items[1].Width));
        Assert.AreNotEqual(Path(x => x.Values[0]), Path(x => x.Values[1]));
    }

    [TestMethod]
    public void KeysAreCaseSensitive()
    {
        Assert.AreNotEqual(Path(x => x.Map["a"].Width), Path(x => x.Map["A"].Width));
    }

    [TestMethod]
    public void TheSameIndexWrittenTwiceIsOnePath()
    {
        var first = Path(x => x.Items[0].Width);
        var second = Path(x => x.Items[0].Width);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreEqual(1, new HashSet<TransitionProperty> { first, second }.Count);
    }

    [TestMethod]
    public void AnIndexArgumentIsIdentityNotValue()
    {
        // 键必须与「当时求值得到什么」解耦：同一个闭包字段先声明再查询，两次解析必须落在同一个键上。
        var idx = 1;

        Assert.AreEqual(Path(x => x.Items[idx].Width), Path(x => x.Items[idx].Width));
    }

    [TestMethod]
    public void AClosureFieldIsNotMistakenForAnother()
    {
        // 循环里逐次声明时每次都是新的闭包实例：按值做键会让这五条路径挤成一个条目，
        // 只剩最后一个动画活下来。
        var state = new StateCore();
        for (var index = 0; index < 5; index++)
        {
            var captured = index;
            state.SetValue((Target x) => x.Items[captured].Width, (double)captured);
        }

        Assert.AreEqual(5, state.Values.Count);
    }

    [TestMethod]
    public void DeclaringAndQueryingAcrossTimeAgree()
    {
        // SetValue 与 TryGetValue 是两个时刻的两次解析。身份若依赖当时的值，这里会静默 miss。
        var idx = 2;
        var state = new StateCore();

        state.SetValue((Target x) => x.Items[idx].Width, 12d);

        Assert.IsTrue(state.TryGetValue((Target x) => x.Items[idx].Width, out double value));
        Assert.AreEqual(12d, value);
    }

    [TestMethod]
    public void OptionsLandOnTheSameKeyAsTheValue()
    {
        var idx = 1;
        var state = new StateCore();

        state.SetValue((Target x) => x.Items[idx].Width, 3d);
        state.SetOptions((Target x) => x.Items[idx].Width, "options");

        Assert.AreEqual(1, state.Values.Count);
        Assert.AreEqual(1, state.Options.Count);
    }

    [TestMethod]
    public void TheLambdaParameterNameIsNotPartOfTheIdentity()
    {
        Assert.IsTrue(TransitionProperty.TryCreate(
            (Expression<Func<Target, double>>)(node => node.Items[node.SelectedIndex].Width), out var first));
        Assert.IsTrue(TransitionProperty.TryCreate(
            (Expression<Func<Target, double>>)(other => other.Items[other.SelectedIndex].Width), out var second));

        Assert.AreEqual(first, second);
        Assert.AreEqual(first!.GetHashCode(), second!.GetHashCode());
    }

    [TestMethod]
    public void AFrozenIndexIsNotTheSamePathAsALiveOne()
    {
        Assert.AreNotEqual(
            Path(x => x.Items[x.SelectedIndex].Width),
            Path(x => x.Items[PathIndex.Frozen(x.SelectedIndex)].Width));
    }

    [TestMethod]
    public void AnAncestorAndItsDescendantConflict()
    {
        var state = new StateCore();
        state.SetValue((Target x) => x.Items[0].Width, 1d);

        Assert.Throws<TransitionPathConflictException>(() => state.SetValue((Target x) => x.Items[0], new Leaf()));
    }

    [TestMethod]
    public void AdjacentIndicesDoNotConflict()
    {
        var state = new StateCore();
        state.SetValue((Target x) => x.Items[0].Width, 1d);
        state.SetValue((Target x) => x.Items[1].Width, 2d);

        Assert.AreEqual(2, state.Values.Count);
    }

    // ── 档位 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void APlainIndexFollowsTheTarget()
    {
        var target = new Target();
        var property = Path(x => x.Items[x.SelectedIndex].Width);

        target.SelectedIndex = 1;
        Assert.IsTrue(property.SetValue(target, 42d));

        target.SelectedIndex = 2;
        Assert.IsTrue(property.SetValue(target, 7d));

        Assert.AreEqual(42d, target.Items[1].Width);
        Assert.AreEqual(7d, target.Items[2].Width);
    }

    [TestMethod]
    public void AFrozenIndexStaysWhereItStarted()
    {
        var target = new Target();
        var property = Path(x => x.Items[PathIndex.Frozen(x.SelectedIndex)].Width);

        target.SelectedIndex = 1;
        var bound = property.BindTo(target);

        target.SelectedIndex = 2;
        Assert.IsTrue(bound.SetValue(target, 99d));

        Assert.AreEqual(99d, target.Items[1].Width);
        Assert.AreEqual(0d, target.Items[2].Width);
    }

    [TestMethod]
    public void AnUnfrozenPathIsNotWrapped()
    {
        var target = new Target();
        var property = Path(x => x.Items[0].Width);

        // 没有冻结实参时不产生包装对象——常见路径不为这个特性付任何代价。
        Assert.AreSame(property, property.BindTo(target));
    }

    // ── 边界 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnOutOfRangeIndexIsSilentRatherThanFatal()
    {
        var target = new Target();
        var tooFar = Path(x => x.Values[10]).BindTo(target);

        // MakeIndex 越界是抛，不是返回未命中；它会在 Prepare 与之后每一帧、在 UI 线程的 dispatcher 回调里抛，
        // 那条路径上没有任何 catch。
        Assert.AreSame(TransitionProperty.UnreadablePath, tooFar.GetValue(target));
        Assert.IsFalse(tooFar.SetValue(target, 1d));
    }

    [TestMethod]
    public void ANonPublicIndexerIsNotASecondClassMember()
    {
        // 可访问性不是判定条件——判定的是「成员存不存在、能不能读写」。索引器不能因为是非公开的，
        // 就成为唯一一种会悄悄失效的成员。
        var target = new Target();

        Assert.IsTrue(TransitionProperty.TryCreate(Target.HiddenIndexerPath, out var property));
        Assert.IsTrue(property!.SetValue(target, 4d));
        Assert.AreEqual(4d, property.GetValue(target));
    }

    [TestMethod]
    public void AReadOnlyIndexerAsALeafReportsItself()
    {
        // 只有路径<b>末段</b>本身决定可写性。只读索引器当末段时没有成员可以写入，于是和只读属性一样不可写——
        // 不截断、不抛异常，就是普通的属性可读写判定。
        var property = Path(x => x.ReadOnly[0]);

        Assert.IsTrue(property.CanRead);
        Assert.IsFalse(property.CanWrite);
    }

    [TestMethod]
    public void AReadOnlyIndexerAsAPathStepIsPerfectlyWritable()
    {
        // 只读索引器只是路上的一步：读它拿到元素，再写元素的公开成员。索引器本身是否可写与这条路径无关。
        var target = new Target();
        var property = Path(x => x.ReadOnlyLeaves[1].Width);

        Assert.IsTrue(property.CanRead);
        Assert.IsTrue(property.CanWrite);
        Assert.IsTrue(property.SetValue(target, 5d));
        Assert.AreEqual(5d, target.ReadOnlyLeaves[1].Width);
    }

    [TestMethod]
    public void AValueTypeIntermediateStillReachesItsReferenceMember()
    {
        // a(值类型).b(引用类型).c(公开可读写)：拆箱拿到的是 a 的副本，但 b 是从副本里读出来的<b>引用</b>，
        // 指向同一对象，所以 c 的写入落在真正的 b 上，路径生效。
        var target = new Target { Holder = new Holder { Leaf = new Leaf() } };
        var property = Path(x => x.Holder.Leaf.Width);

        Assert.IsTrue(property.CanWrite);
        Assert.IsTrue(property.SetValue(target, 7d));
        Assert.AreEqual(7d, target.Holder.Leaf.Width);
    }

    [TestMethod]
    public void ADataSetKeyedOnTheIndexDoesNotThrow()
    {
        var target = new Target();
        var missing = Path(x => x.Map["absent"].Width);

        Assert.AreSame(TransitionProperty.UnreadablePath, missing.GetValue(target));
        Assert.IsFalse(missing.SetValue(target, 1d));
    }

    [TestMethod]
    public void AnUnrepresentableArgumentIsDropped()
    {
        // 表达不出稳定身份的实参必须让整条路径被丢弃，绝不输出被截断的键。
        Assert.IsFalse(TransitionProperty.TryCreate(
            (Expression<Func<Target, double>>)(x => x.Map[new string('a', 1)].Width), out _));
    }

    [TestMethod]
    public void MembersAcceptsIndexerPaths()
    {
        var members = TransitionProperty.Members<Target>(x => x.Items[0].Width, x => x.Values[1]);

        Assert.AreEqual(2, members.Count);
        Assert.AreEqual("Items[0].Width", members[0].Path);
        Assert.AreEqual("Values[1]", members[1].Path);
    }

    // ── 装配（Prepare → SamplerSet）─────────────────────────────────────────

    [TestMethod]
    public void PrepareFreezesTheIndexBeforeAnyFrameIsWritten()
    {
        var target = new Target();
        target.Items[1].Width = 10d;

        var state = new StateCore();
        state.SetValue((Target x) => x.Items[PathIndex.Frozen(x.SelectedIndex)].Width, 100d);
        target.SelectedIndex = 1;

        var set = new TestInterpolator().Prepare(target, state, new TransitionEffectCore(), new ImmediateInspector());

        target.SelectedIndex = 2;
        set.Apply(target, 1d);

        Assert.AreEqual(100d, target.Items[1].Width);
        Assert.AreEqual(0d, target.Items[2].Width);
    }

    [TestMethod]
    public void PrepareLeavesAPlainIndexFollowing()
    {
        var target = new Target();
        target.Items[1].Width = 10d;

        var state = new StateCore();
        state.SetValue((Target x) => x.Items[x.SelectedIndex].Width, 100d);
        target.SelectedIndex = 1;

        var set = new TestInterpolator().Prepare(target, state, new TransitionEffectCore(), new ImmediateInspector());

        target.SelectedIndex = 2;
        set.Apply(target, 1d);

        Assert.AreEqual(100d, target.Items[2].Width);
        Assert.AreEqual(10d, target.Items[1].Width);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private sealed class ImmediateInspector : ImmediateHost
    {
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private static TransitionProperty Path<TValue>(Expression<Func<Target, TValue>> expression)
    {
        Assert.IsTrue(TransitionProperty.TryCreate(expression, out var property), $"not parsed: {expression}");
        return property!;
    }

    private sealed class Leaf
    {
        public double Width { get; set; }
    }

    /// <summary>值类型中间环节：<c>b</c> 是它的一个引用类型属性，<c>c</c> 是 <c>b</c> 上的公开可读写属性。</summary>
    private struct Holder
    {
        public Leaf Leaf { get; set; }
    }

    private sealed class Matrix
    {
        private readonly double[] _cells = new double[9];

        public double this[int row, int column]
        {
            get => _cells[row * 3 + column];
            set => _cells[row * 3 + column] = value;
        }
    }

    private sealed class Target
    {
        public List<Leaf> Items { get; } = [new Leaf(), new Leaf(), new Leaf()];

        public Dictionary<string, Leaf> Map { get; } = [];

        public double[] Values { get; set; } = [1d, 2d, 3d];

        public double[,] Grid { get; set; } = new double[2, 2];

        public Matrix Cells { get; } = new();

        public IReadOnlyList<double> ReadOnly { get; } = new List<double> { 1d, 2d };

        public IReadOnlyList<Leaf> ReadOnlyLeaves { get; } = new List<Leaf> { new Leaf(), new Leaf() };

        public Holder Holder { get; set; }

        public int SelectedIndex { get; set; }

        private readonly double[] _hidden = new double[3];

        private double this[int index]
        {
            get => _hidden[index];
            set => _hidden[index] = value;
        }

        /// <summary>私有索引器只有本类型能写进表达式树，所以路径从这里取。</summary>
        public static Expression<Func<Target, double>> HiddenIndexerPath => x => x[0];
    }
}
