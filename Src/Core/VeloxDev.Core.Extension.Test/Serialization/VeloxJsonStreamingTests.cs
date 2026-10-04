using System.IO;
using System.Linq;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The reader produces the same graph whether it holds the whole document or pulls it in pieces.
/// </summary>
/// <remarks>
/// <para>
/// The refill boundaries are the whole risk of a streaming reader, and they are invisible to every other
/// test: a document read in one piece never reaches the code that compacts, grows, or compares across a
/// window edge. So the same frozen documents are pushed through a source that hands over one character at a
/// time — every token then straddles a refill — and through buffers deliberately smaller than a single
/// member name.
/// </para>
/// <para>
/// The assertion is idempotence (<c>write(read(golden)) == golden</c>), which is stronger than "it parsed":
/// a boundary bug that silently drops or truncates a value changes the graph, and the re-serialised bytes
/// stop matching.
/// </para>
/// </remarks>
[TestClass]
public class VeloxJsonStreamingTests
{
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

    private static (string Name, Type Type)[] Corpus =>
    [
        ("tree", typeof(TreeDefaultViewModel)),
        ("canvas-layout", typeof(CanvasLayout)),
        ("slot-enumerator", typeof(SlotEnumerator<SlotDefaultViewModel>)),
        ("compiled-graph", typeof(CompiledGraph)),
    ];

    // 编译图那份快照是带着排除集写出来的，回写必须用同一对入口，否则比的是两种文档。
    private static string Reserialize(object graph)
        => graph is CompiledGraph compiled
            ? compiled.SerializeCompiledGraph()
            : VeloxJsonSerializer.Serialize(graph);

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(5)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(13)]
    [DataRow(64)]
    public void EveryChunkSize_ReproducesTheFrozenDocument(int chunk)
    {
        foreach (var (name, type) in Corpus)
        {
            var frozen = Golden(name);

            // chunk = 1 时每个 token 都跨越一次续读。
            var restored = VeloxJsonSerializer.Deserialize(new ChunkedTextReader(frozen, chunk), type);

            Assert.IsNotNull(restored, $"{name}: chunk {chunk} produced nothing");
            Assert.AreEqual(frozen, Reserialize(restored), $"{name}: chunk {chunk} changed the document");
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(16)]
    public void EveryBufferSize_ReproducesTheFrozenDocument(int bufferSize)
    {
        foreach (var (name, type) in Corpus)
        {
            var frozen = Golden(name);

            // 缓冲比成员名还小，逼出「一个 token 比整个窗口长」的增长路径。
            var reader = new VeloxJsonReader(new ChunkedTextReader(frozen, 1), bufferSize);
            var restored = VeloxJsonSerializer.ReadValue(reader, type, null);

            Assert.IsNotNull(restored, $"{name}: buffer {bufferSize} produced nothing");
            Assert.AreEqual(frozen, Reserialize(restored), $"{name}: buffer {bufferSize} changed the document");
        }
    }

    [TestMethod]
    public void AStreamedDocument_MatchesTheOneReadFromAWholeString()
    {
        foreach (var (name, type) in Corpus)
        {
            var frozen = Golden(name);

            var wholeString = VeloxJsonSerializer.Deserialize(frozen, type);
            var streamed = VeloxJsonSerializer.Deserialize(new ChunkedTextReader(frozen, 3), type);

            Assert.IsNotNull(wholeString, $"{name}: the whole-string read produced nothing");
            Assert.IsNotNull(streamed, $"{name}: the buffered read produced nothing");

            Assert.AreEqual(
                VeloxJsonSerializer.Serialize(wholeString),
                VeloxJsonSerializer.Serialize(streamed),
                $"{name}: the two sources disagree");
        }
    }

    [TestMethod]
    public void AnEmptySource_IsRefusedTheSameWayAnEmptyStringIs()
    {
        // 两个入口对同一个错误必须给同一个异常，否则调用方要为入口各写一条分支。
        Assert.ThrowsExactly<ArgumentException>(
            () => VeloxJsonSerializer.Deserialize(new StringReader(string.Empty), typeof(TreeDefaultViewModel)));

        Assert.ThrowsExactly<ArgumentException>(
            () => VeloxJsonSerializer.Deserialize(string.Empty, typeof(TreeDefaultViewModel)));
    }

    [TestMethod]
    public void AMemberBeginningLikeAReference_SurvivesARefill()
    {
        // "$reff" 以 "$ref" 开头但不是引用：探测会读进去再整体回退，而回退位置必须跨压缩保住。
        // 把每段压到 1 个字符，让探测的每一步都落在边界上。
        var json = "{\"$id\":\"1\",\"$type\":\"X\",\"$reff\":1,\"Value\":42}";

        var reader = new VeloxJsonReader(new ChunkedTextReader(json, 1), 1);

        Assert.IsTrue(reader.BeginObject(out var id, out var typeName));
        Assert.AreEqual(1, id);
        Assert.IsNotNull(typeName);

        var seen = 0;
        while (reader.NextMember())
        {
            if (reader.MemberNameEquals("$reff")) { seen = reader.ReadInt32(); }
            else reader.SkipValue();
        }
        reader.FinishObject();

        Assert.AreEqual(1, seen, "the member that only looks like a reference must round-trip");
    }

    [TestMethod]
    public void MalformedDocuments_AreRefusedRatherThanMisread()
    {
        // 每一条都在续读边界上截断，所以走的是「缓冲用尽」那条路而不是长度检查。
        Assert.ThrowsExactly<FormatException>(() => Read("{\"a\":\"unterminated"));
        Assert.ThrowsExactly<FormatException>(() => Read("{\"a\":\"bad\\q\"}"));
        Assert.ThrowsExactly<FormatException>(() => Read("{\"a\":\"\\u12"));
        // 位置缺值。注意不能拿 `{"a":tru}` 当反例：未知成员一律被 SkipValue 跳过，
        // 而跳过不校验字面量 —— 那条路本来就不该抛。
        Assert.ThrowsExactly<FormatException>(() => Read("{\"a\":}"));
        Assert.ThrowsExactly<FormatException>(() => Read("{\"$id\":\"x\"}"));

        static object? Read(string json)
        {
            var reader = new VeloxJsonReader(new ChunkedTextReader(json, 1), 1);
            return VeloxJsonSerializer.ReadValue(reader, typeof(TreeDefaultViewModel), null);
        }
    }

    // ── 异步面 ────────────────────────────────────────────────────────────────────────────────────────
    // 同一批语料在异步读写器上再跑一遍。生成器为每个类型产出同步与异步两套，两条路必须同义 ——
    // 它们各自独立演化过一次就够受了，这几条测试就是把它们钉在一起的那颗钉子。

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(7)]
    [DataRow(64)]
    public async Task EveryChunkSize_ReproducesTheFrozenDocument_OnTheAsyncPath(int chunk)
    {
        foreach (var (name, type) in Corpus)
        {
            var frozen = Golden(name);

            var restored = await VeloxJsonSerializer.DeserializeAsync(new ChunkedTextReader(frozen, chunk), type);

            Assert.IsNotNull(restored, $"{name}: chunk {chunk} produced nothing on the async path");
            Assert.AreEqual(frozen, Reserialize(restored), $"{name}: chunk {chunk} changed the document on the async path");
        }
    }

    [TestMethod]
    public async Task TheAsyncWriter_ProducesTheSameBytesAsTheSyncOne()
    {
        foreach (var (name, type) in Corpus)
        {
            var graph = VeloxJsonSerializer.Deserialize(Golden(name), type);
            Assert.IsNotNull(graph);

            using var text = new StringWriter();
            await VeloxJsonSerializer.WriteToAsync(text, graph);

            Assert.AreEqual(
                VeloxJsonSerializer.Serialize(graph), text.ToString(),
                $"{name}: the async writer disagrees with the sync one");
        }
    }

    [TestMethod]
    public async Task AStreamRoundTrip_MatchesTheFrozenDocument()
    {
        var frozen = Golden("tree");
        var graph = VeloxJsonSerializer.Deserialize(frozen, typeof(TreeDefaultViewModel));
        Assert.IsNotNull(graph);

        using var stream = new MemoryStream();
        await VeloxJsonSerializer.WriteToAsync(stream, graph);

        stream.Position = 0;
        var restored = await VeloxJsonSerializer.DeserializeAsync(stream, typeof(TreeDefaultViewModel));

        Assert.IsNotNull(restored, "the stream round trip produced nothing");
        Assert.AreEqual(frozen, VeloxJsonSerializer.Serialize(restored), "the stream round trip changed the document");
    }

    [TestMethod]
    public async Task TheAsyncEntryPoints_RejectAnEmptySourceTheSameWayTheSyncOnesDo()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => VeloxJsonSerializer.DeserializeAsync(new StringReader(string.Empty), typeof(TreeDefaultViewModel)));
    }
}
