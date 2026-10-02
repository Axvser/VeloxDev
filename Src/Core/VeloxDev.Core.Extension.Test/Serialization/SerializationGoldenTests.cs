using System.IO;
using System.Runtime.CompilerServices;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Freezes the exact bytes today's serializer produces, so the source-generated replacement can be held to
/// 「byte for byte」 rather than to a round-trip.
/// </summary>
/// <remarks>
/// <para>
/// A round-trip test cannot see a changed property order, a renumbered <c>$id</c>, a re-quoted <c>NaN</c> or a
/// two-space indent that became four. The corpus is deliberately made of deterministic inputs — no
/// <c>Guid.NewGuid()</c> reaches the payload, because every serialized id in these types is either computed
/// from fixed inputs or excluded by the writable-only contract — so the comparison can be exact.
/// </para>
/// <para>
/// Regenerate deliberately, never automatically: set <c>VELOX_CAPTURE_GOLDEN=1</c> and run these tests. Without
/// the variable a missing file is a failure, so a golden can never be created by accident.
/// </para>
/// </remarks>
[TestClass]
public class SerializationGoldenTests
{
    private const string CaptureVariable = "VELOX_CAPTURE_GOLDEN";

    private static string GoldenDirectory
    {
        get
        {
            // 从测试二进制目录往上找到源码目录：黄金文件要跟着源码走，不跟着 bin 走。
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VeloxDev.Core.Extension.Test.csproj")))
                directory = directory.Parent;

            Assert.IsNotNull(directory, "could not locate the test project directory from " + AppContext.BaseDirectory);
            return Path.Combine(directory!.FullName, "Serialization", "Golden");
        }
    }

    private static void AssertMatchesGolden(string name, string json, [CallerMemberName] string? capturedBy = null)
    {
        var directory = GoldenDirectory;
        var path = Path.Combine(directory, name + ".json");

        if (Environment.GetEnvironmentVariable(CaptureVariable) == "1")
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
            Assert.Inconclusive($"captured {name} ({capturedBy}) — rerun without {CaptureVariable}");
        }

        Assert.IsTrue(File.Exists(path), $"no golden file at {path}; capture it with {CaptureVariable}=1");
        Assert.AreEqual(File.ReadAllText(path), json, $"{name}: the archive format must not change");
    }

    /// <summary>
    /// A mounted two-node tree: reference ids for the repeated <c>Parent</c> back-references, the interface-keyed
    /// <c>LinksMap</c>, and the world-coordinate expansion the geometry hooks perform.
    /// </summary>
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
    public void AWholeTree_MatchesItsGoldenFile()
        => AssertMatchesGolden("tree", BuildTree().Serialize());

    [TestMethod]
    public void ASlotEnumerator_MatchesItsGoldenFile()
    {
        // 枚举器实现 IEnumerable，却被写成对象而不是数组 —— 这条只有黄金文件看得见。
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        AssertMatchesGolden("slot-enumerator", enumerator.Serialize());
    }

    [TestMethod]
    public void ACanvasLayout_MatchesItsGoldenFile()
    {
        var layout = new CanvasLayout { Scale = new Scale(2.5, 3.5), NegativeOffset = new Offset(50, 30) };
        AssertMatchesGolden("canvas-layout", layout.Serialize());
    }

    [TestMethod]
    public void ACompiledGraph_MatchesItsGoldenFile()
    {
        var graph = new CompiledGraph();
        graph.Entries.Add(new ChainSegment { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") });

        AssertMatchesGolden("compiled-graph", CompiledGraphEx.SerializeCompiledGraph(graph, includeTree: false));
    }

    [TestMethod]
    public void TheGoldenFilesAreStillReadableByTheCurrentImplementation()
    {
        // 冻结的字节能被读回来 —— 黄金文件本身也要是活的，否则它只是文本。
        var tree = File.ReadAllText(Path.Combine(GoldenDirectory, "tree.json")).Deserialize<TreeDefaultViewModel>();
        Assert.IsNotNull(tree);
        Assert.HasCount(2, tree.Nodes, "the frozen document must still contain both nodes");
    }
}
