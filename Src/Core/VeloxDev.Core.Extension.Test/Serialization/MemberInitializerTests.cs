using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// A member whose initializer already holds something, read from a document that says something else.
/// </summary>
/// <remarks>
/// <para>
/// A load says what the member holds <b>now</b>, not what to add to it. Every member here starts with a value
/// that the document does not have, so "the document replaced it" and "the document was merged into it" give
/// different answers — and the difference is a byte in the document written back out.
/// </para>
/// <para>
/// The corpus cannot see this: every collection in it starts empty, so appending and replacing agree. The
/// assertion that matters is idempotence — <c>write(read(document)) == document</c> — because a stray element or
/// a stale key shows up there and nowhere else.
/// </para>
/// </remarks>
[TestClass]
public partial class MemberInitializerTests
{
    /// <summary>Every member starts holding something the document does not have.</summary>
    internal sealed partial class PrepopulatedModel
    {
        [VeloxProperty] private List<int> list = [9];
        [VeloxProperty] private HashSet<int> set = [9];
        [VeloxProperty] private Dictionary<string, int> map = new() { ["stale"] = 9 };
        [VeloxProperty] private Dictionary<string, List<int>> nested = new() { ["stale"] = [9] };

        // 初值比文档**长**：定长数组清不掉也加不进，只能看读侧是不是整只换掉 —— 留着尾巴就是多出来的字节。
        [VeloxProperty] private int[] numbers = [9, 9, 9, 9];
        [VeloxProperty] private byte[] payload = [9, 9, 9, 9];

        // 另外两种容器套容器。
        [VeloxProperty] private Dictionary<int, string> keyed = new() { [7] = "stale" };
        [VeloxProperty] private List<Dictionary<string, int>> mapsInList = [new() { ["stale"] = 9 }];
    }

    /// <summary>The other side: contents that share no element or key with the initializers above.</summary>
    private static PrepopulatedModel FromTheDocument() => new()
    {
        List = [1, 2],
        Set = [1, 2],
        Map = new() { ["kept"] = 1 },
        Nested = new() { ["kept"] = [1, 2] },
        Numbers = [1, 2],
        Payload = [1, 2],
        Keyed = new() { [1] = "one" },
        MapsInList = [new() { ["kept"] = 1 }],
    };

    private static void AssertHoldsWhatTheDocumentSaid(PrepopulatedModel restored, string entry)
    {
        CollectionAssert.AreEqual(new[] { 1, 2 }, restored.List,
            entry + ": the list member must hold what the document says, not what the initializer left");

        CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Set.OrderBy(x => x).ToArray(),
            entry + ": the set member");

        CollectionAssert.AreEqual(new[] { "kept" }, restored.Map.Keys.OrderBy(x => x).ToArray(),
            entry + ": a key the document does not have must not survive the load");

        CollectionAssert.AreEqual(new[] { "kept" }, restored.Nested.Keys.ToArray(),
            entry + ": and neither must one inside a nested container");
        CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Nested["kept"], entry + ": the nested list");

        CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Numbers,
            entry + ": a fixed-size array must be exactly as long as the document's");
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, restored.Payload,
            entry + ": and so must byte[]");

        CollectionAssert.AreEqual(new[] { 1 }, restored.Keyed.Keys.ToArray(),
            entry + ": a stale key in a typed dictionary");
        Assert.AreEqual(1, restored.MapsInList.Count, entry + ": a list of maps is not appended to");
        CollectionAssert.AreEqual(new[] { "kept" }, restored.MapsInList[0].Keys.ToArray(), entry + ": nor is the map inside it");
    }

    [TestMethod]
    public void ALoad_LeavesExactlyWhatTheDocumentSaid()
    {
        var document = VeloxJsonSerializer.Serialize(FromTheDocument());

        AssertHoldsWhatTheDocumentSaid(VeloxJsonSerializer.Deserialize<PrepopulatedModel>(document)!, "string");
    }

    [TestMethod]
    public async Task ALoad_LeavesExactlyWhatTheDocumentSaidOnTheAsyncFace()
    {
        var document = VeloxJsonSerializer.Serialize(FromTheDocument());

        var restored = await VeloxJsonSerializer.DeserializeAsync<PrepopulatedModel>(new ChunkedTextReader(document, 1));

        AssertHoldsWhatTheDocumentSaid(restored!, "chunked TextReader, async");
    }

    /// <summary>The same members, empty — a document that says "nothing here" over initializers that say otherwise.</summary>
    private static PrepopulatedModel Nothing() => new()
    {
        List = [],
        Set = [],
        Map = new(),
        Nested = new(),
        Numbers = [],
        Payload = [],
        Keyed = new(),
        MapsInList = [],
    };

    [TestMethod]
    public void ADocumentThatHoldsNothing_EmptiesWhatTheInitializerPutThere()
    {
        // 与第一组相反的方向：那边文档比初值多，这边文档比初值少 —— 两边的「剩下什么」都要由文档说了算。
        var document = VeloxJsonSerializer.Serialize(Nothing());

        var restored = VeloxJsonSerializer.Deserialize<PrepopulatedModel>(document)!;

        Assert.AreEqual(0, restored.List.Count, "an empty document empties the list");
        Assert.AreEqual(0, restored.Set.Count, "and the set");
        Assert.AreEqual(0, restored.Map.Count, "and the map");
        Assert.AreEqual(0, restored.Nested.Count, "and the nested one");
        Assert.AreEqual(0, restored.Numbers.Length, "and the fixed-size array");
        Assert.AreEqual(0, restored.Payload.Length, "and byte[]");
        Assert.AreEqual(0, restored.Keyed.Count, "and the typed dictionary");
        Assert.AreEqual(0, restored.MapsInList.Count, "and the list of maps");

        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(restored), "and the bytes come back the same");
    }

    [TestMethod]
    public void ReadingTwice_IntoTheSameInstance_DoesNotAccumulate()
    {
        // 把同一份文档读进一个已经装着的实例（`ReadValue` 的 existing 重载，生成代码用的就是它）——
        // 任何残留的追加或合并都会在第二遍显形。
        var document = VeloxJsonSerializer.Serialize(FromTheDocument());
        var target = FromTheDocument();

        for (var pass = 0; pass < 2; pass++)
        {
            VeloxJsonSerializer.ReadValue(new VeloxJsonReader(document), typeof(PrepopulatedModel), target);
        }

        AssertHoldsWhatTheDocumentSaid(target, "read twice");
        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(target), "a second read changed the instance");
    }

    [TestMethod]
    public void ALoad_ReproducesTheDocumentByteForByte()
    {
        // 这是真正的判据：初值留下的任何多余元素或陈旧键，回写出来都比原文多出字节。
        var document = VeloxJsonSerializer.Serialize(FromTheDocument());

        var restored = VeloxJsonSerializer.Deserialize<PrepopulatedModel>(document)!;

        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(restored), "the document changed on a load");
    }

    [TestMethod]
    public async Task ALoad_ReproducesTheDocumentByteForByteOnTheAsyncFace()
    {
        var document = VeloxJsonSerializer.Serialize(FromTheDocument());

        var restored = await VeloxJsonSerializer.DeserializeAsync<PrepopulatedModel>(new ChunkedTextReader(document, 1));

        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(restored!), "the document changed on an async load");
    }
}
