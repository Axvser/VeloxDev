using System.IO;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Reading a frozen document and writing it straight back reproduces it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The other golden tests pin <i>write(read model)</i>; this pins <i>write(read frozen document)</i>. The two are
/// not the same gate: a reader that quietly drops a member, re-numbers a reference or restores a value into a
/// different member still produces a document the writer test would accept, because that test never crosses the
/// reader at all. Idempotence is the only check that sees both halves at once.
/// </para>
/// <para>
/// The documents are read raw — no line-ending normalisation — so this is an exact byte comparison and it also
/// witnesses that the writer keeps <see cref="System.Environment.NewLine"/>. Normalising here would make the
/// assertion pass on a writer that emits a hardcoded <c>"\n"</c>, which is exactly the failure it exists to catch.
/// </para>
/// </remarks>
[TestClass]
public class SerializationIdempotenceTests
{
    // 黄金文件要跟着源码走，不跟着 bin 走 —— 与 SerializationGoldenTests.GoldenDirectory 同一套定位。
    private static string GoldenDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VeloxDev.Core.Extension.Test.csproj")))
                directory = directory.Parent;

            Assert.IsNotNull(directory, "could not locate the test project directory from " + AppContext.BaseDirectory);
            return Path.Combine(directory!.FullName, "Serialization", "Golden");
        }
    }

    private static string Golden(string name) => File.ReadAllText(Path.Combine(GoldenDirectory, name + ".json"));

    [TestMethod]
    public void TheTreeDocument_IsReproducedFromWhatTheReaderMadeOfIt()
    {
        var frozen = Golden("tree");

        var tree = VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(frozen);

        Assert.IsNotNull(tree);
        Assert.AreEqual(frozen, VeloxJsonSerializer.Serialize(tree), "write(read(tree.json)) must equal tree.json");
    }

    [TestMethod]
    public void TheCanvasLayoutDocument_IsReproducedFromWhatTheReaderMadeOfIt()
    {
        var frozen = Golden("canvas-layout");

        var layout = VeloxJsonSerializer.Deserialize<CanvasLayout>(frozen);

        Assert.IsNotNull(layout);
        Assert.AreEqual(frozen, VeloxJsonSerializer.Serialize(layout), "write(read(canvas-layout.json)) must equal canvas-layout.json");
    }

    [TestMethod]
    public void TheSlotEnumeratorDocument_IsReproducedFromWhatTheReaderMadeOfIt()
    {
        // 枚举器实现 IEnumerable，却被写成对象而不是数组；读回来若走了数组那条路，这里就不再相等。
        var frozen = Golden("slot-enumerator");

        var enumerator = VeloxJsonSerializer.Deserialize<SlotEnumerator<SlotDefaultViewModel>>(frozen);

        Assert.IsNotNull(enumerator);
        Assert.AreEqual(frozen, VeloxJsonSerializer.Serialize(enumerator), "write(read(slot-enumerator.json)) must equal slot-enumerator.json");
    }

    [TestMethod]
    public void TheCompiledGraphDocument_IsReproducedFromWhatTheReaderMadeOfIt()
    {
        // 走 CompiledGraphEx 这一对：快照模式在写侧带着排除集，读侧不带，所以这条也钉住了两侧的不对称是自洽的。
        var frozen = Golden("compiled-graph");

        var graph = frozen.DeserializeCompiledGraph();

        Assert.IsNotNull(graph);
        Assert.AreEqual(frozen, graph.SerializeCompiledGraph(), "write(read(compiled-graph.json)) must equal compiled-graph.json");
    }
}
