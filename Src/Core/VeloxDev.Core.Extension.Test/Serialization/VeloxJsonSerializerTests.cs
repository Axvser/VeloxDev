using System.IO;
using VeloxDev.MVVM.Serialization;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Holds the generated serializer to the frozen documents.
/// </summary>
/// <remarks>
/// <para>
/// The goldens were captured from the serializer this one replaces, so these are the byte-for-byte tests: a
/// member reordered, a reference renumbered or a number respelled shows up here and nowhere else.
/// </para>
/// <para>
/// Nothing is registered by hand — the writers come from the generator's module initializer, which is the whole
/// point: if the generated registration did not run, these tests could not pass.
/// </para>
/// </remarks>
[TestClass]
public class VeloxJsonSerializerTests
{
    private static string Golden(string name)
        => File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(typeof(VeloxJsonSerializerTests).Assembly.Location)!, "..", "..", "..",
            "Serialization", "Golden", name + ".json"));

    /// <summary>Compares ignoring line endings — the goldens are checked in as LF, the writer emits the platform's.</summary>
    private static void AssertMatchesGolden(string name, string actual)
        => Assert.AreEqual(
            Golden(name).Replace("\r\n", "\n"),
            actual.Replace("\r\n", "\n"),
            $"{name}: the generated serializer must reproduce the frozen document exactly");

    [TestMethod]
    public void ACanvasLayout_ComesOutByteForByte()
    {
        var layout = new CanvasLayout { Scale = new Scale(2.5, 3.5), NegativeOffset = new Offset(50, 30) };
        AssertMatchesGolden("canvas-layout", VeloxJsonSerializer.Serialize(layout));
    }

    /// <summary>A mounted two-node tree with one slot each — the shape the goldens were captured from.</summary>
    private static TreeDefaultViewModel BuildTree()
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(2d, 2d);

        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        node.Anchor = new Anchor(1400d, 900d, 0);
        node.Size = new Size(300d, 200d);
        node.CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneTarget });

        var other = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(other);
        other.Anchor = new Anchor(200d, 100d, 0);
        other.CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneSource });

        return tree;
    }

    [TestMethod]
    public void AWholeTree_ComesOutByteForByte()
        => AssertMatchesGolden("tree", VeloxJsonSerializer.Serialize(BuildTree()));

    [TestMethod]
    public void TheFrozenDocument_ReadsBackThroughTheGeneratedReader()
    {
        var tree = VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(Golden("tree").Replace("\r\n", "\n"));

        Assert.IsNotNull(tree);
        Assert.HasCount(2, tree.Nodes, "the frozen document holds two nodes");

        var first = (NodeDefaultViewModel)tree.Nodes[0];

        // 文件里存的是世界坐标 1400；getter 按画布缩放（2.0）折叠一次，所以读出来是 700 ——
        // 折叠恰好一次正是这条契约要保的东西。
        Assert.AreEqual(700d, first.Anchor.Horizontal, 1e-9, "the world coordinate must be collapsed exactly once");
        Assert.AreEqual(150d, first.Size.Width, 1e-9);
        Assert.AreSame(tree, first.Parent, "$ref must resolve to the node's parent, not a copy");
    }

    [TestMethod]
    public void TheTreeRoundTripsThroughTheGeneratedSerializer()
    {
        var original = BuildTree();

        var restored = VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(
            VeloxJsonSerializer.Serialize(original));

        Assert.IsNotNull(restored);
        Assert.HasCount(2, restored.Nodes);
        Assert.AreEqual(new Scale(2d, 2d), restored.Layout.Scale, "the zoom level survives");
        Assert.AreEqual(700d, ((NodeDefaultViewModel)restored.Nodes[0]).Anchor.Horizontal, 1e-9,
            "and the world coordinate the hooks expand on the way out comes back collapsed exactly once");
    }

    [TestMethod]
    public void TheTwoSerializers_ReadEachOthersDocuments()
    {
        // 互读：旧实现写的，新实现读得回来；新实现写的，旧实现读得回来。
        var original = BuildTree();

        var byOldWriter = original.Serialize();
        var readByNew = VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(byOldWriter);
        Assert.HasCount(2, readByNew!.Nodes, "the generated reader must read a file the old serializer wrote");

        var byNewWriter = VeloxJsonSerializer.Serialize(original);
        var readByOld = byNewWriter.Deserialize<TreeDefaultViewModel>();
        Assert.HasCount(2, readByOld.Nodes, "the old serializer must read a file the generated reader wrote");
    }

    [TestMethod]
    public void TheGeneratedSerializerIsRegisteredByItsModuleInitializer()
    {
        // 反向对照：模块初始化器没跑的话，上面两条根本走不到。
        Assert.IsNotNull(
            VeloxJsonRegistry.WriterFor(typeof(TreeDefaultViewModel)),
            "the generated registration must have run when the assembly loaded");
        Assert.AreEqual(
            "VeloxDev.WorkflowSystem.TreeDefaultViewModel, VeloxDev.Core",
            VeloxJsonRegistry.NameOf(typeof(TreeDefaultViewModel)),
            "the written type name is the wire format and must not drift");
    }

    [TestMethod]
    public void ANonFiniteNumber_IsWrittenAsAString()
    {
        var writer = new StringWriter();
        var json = new VeloxJsonWriter(writer, indented: false);

        json.WriteStartArray();
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 2d, 2.5 })
        {
            json.WriteNextElement();
            json.WriteDouble(value);
        }
        json.WriteEndArray();

        Assert.AreEqual("""["NaN","Infinity","-Infinity",2.0,2.5]""", writer.ToString());
    }

    [TestMethod]
    public void ATypeWithNoGeneratedWriter_IsRefusedRatherThanGuessedAt()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => VeloxJsonSerializer.Serialize(new NotASerializableType()));

        StringAssert.Contains(error.Message, "no registered JSON writer");
    }

    /// <summary>Nothing the generator knows about — the closed world's outer edge.</summary>
    public sealed class NotASerializableType
    {
        public int Value { get; set; }
    }
}
