using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The asynchronous serializer's dispatch corners — one per synchronous test in
/// <see cref="SerializerEdgeCaseTests"/>, plus the shape-driven read that only the asynchronous chain reaches.
/// </summary>
[TestClass]
public class AsyncSerializerEdgeCaseTests
{
    private static Task<object?> ReadAsync(string document, Type declaredType)
        => VeloxJsonSerializer.ReadValueAsync(new VeloxJsonReader(document), declaredType, null);

    // ── 守卫 ─────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TheGuards_RefuseWhatTheyMust()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteValueAsync(null!, new object(), null));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadUnknownAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteMapAsync(null!, new Hashtable(), typeof(Hashtable), typeof(object), false));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteMapAsync(new VeloxJsonWriter(new StringWriter()), null!, typeof(Hashtable), typeof(object), false));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadArrayAsync(null!, new List<int>(), typeof(int)));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadArrayAsync(new VeloxJsonReader("[]"), null!, typeof(int)));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadMapAsync(null!, new Hashtable(), typeof(string), typeof(int), false));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadMapAsync(new VeloxJsonReader("{}"), null!, typeof(string), typeof(int), false));
    }

    [TestMethod]
    public async Task AValueNothingCanWrite_IsRefused()
    {
        var writer = new VeloxJsonWriter(new StringWriter(), indented: false, async: true);

        // 既不是标量、也没有生成的条目、也不是容器 —— 闭世界之外的类型，错误信息自己说明原因。
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => VeloxJsonSerializer.WriteValueAsync(writer, new NotSerializable(), null));
    }

    [TestMethod]
    public async Task TheExclusionList_IsFlattenedOnlyWhenItHasEntries()
    {
        foreach (var exclusions in new[] { new[] { typeof(int) }, Array.Empty<Type>(), null })
        {
            var text = new StringWriter();
            await VeloxJsonSerializer.WriteToAsync(text, "hello", indented: false, exclusions);
            Assert.AreEqual("\"hello\"", text.ToString(), "a scalar document is the same whatever is excluded");
        }
    }

    // ── 运行期才知道的标量 ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task EveryScalarPrimitive_ReadsByItsDeclaredType()
    {
        Assert.AreEqual("text", await ReadAsync("\"text\"", typeof(string)));
        Assert.AreEqual(true, await ReadAsync("true", typeof(bool)));
        Assert.AreEqual(1, await ReadAsync("1", typeof(int)));
        Assert.AreEqual(2L, await ReadAsync("2", typeof(long)));
        Assert.AreEqual(1.5, await ReadAsync("1.5", typeof(double)));
        Assert.AreEqual(2.5f, await ReadAsync("2.5", typeof(float)));
        Assert.AreEqual(3.5m, await ReadAsync("3.5", typeof(decimal)));
        Assert.AreEqual((byte)4, await ReadAsync("4", typeof(byte)));
        Assert.AreEqual((short)5, await ReadAsync("5", typeof(short)));
        Assert.AreEqual('x', await ReadAsync("\"x\"", typeof(char)));
        Assert.AreEqual(Guid.Empty, await ReadAsync("\"00000000-0000-0000-0000-000000000000\"", typeof(Guid)));
    }

    [TestMethod]
    public async Task TheTwoValuesWhoseKindIsPartOfTheValue_RoundTripThroughTheRuntimeRead()
    {
        // 两条链路必须同义：时间必须按 RoundtripKind 读，否则「…Z」会变成当地时刻。
        var moment = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        var readMoment = (DateTime)(await ReadAsync("\"2026-10-04T12:00:00.0000000Z\"", typeof(DateTime)))!;
        Assert.AreEqual(moment, readMoment);
        Assert.AreEqual(DateTimeKind.Utc, readMoment.Kind);

        Assert.AreEqual(TimeSpan.FromMinutes(45), await ReadAsync("\"00:45:00\"", typeof(TimeSpan)));
    }

    [TestMethod]
    public async Task Binary_ReadsBackAsBytesOnTheAsyncFaceToo()
    {
        // 同步面的运行期标量表有 `byte[]` 这一条；异步面一度漏了它（写入是 base64、读取却按数字数组），
        // 这条就是那处不对称的守卫。
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 250 }, (byte[])(await ReadAsync("\"AQL6\"", typeof(byte[])))!);
    }

    // ── 形状驱动的读 ─────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AnObjectWithNoReaderOfItsOwn_IsReadAsAMap()
    {
        // `object` 成员里放 BCL 容器时走的就是这一条：文档带 `$type`，注册表里没有它，于是按形状读。
        var read = (Dictionary<string, object?>)(
            await ReadAsync("{\"$id\": \"1\", \"$type\": \"No.Such.Type\", \"a\": 1, \"b\": [true, \"x\"]}", typeof(object)))!;

        Assert.AreEqual(1L, read["a"]);
        CollectionAssert.AreEqual(new object?[] { true, "x" }, (List<object?>)read["b"]!);
    }

    [TestMethod]
    public async Task AnObjectWithNoTypeNameAtAll_IsReadAsAMap()
    {
        var read = (Dictionary<string, object?>)(await ReadAsync("{\"$id\": \"1\", \"a\": 1}", typeof(object)))!;

        Assert.AreEqual(1L, read["a"]);
    }

    [TestMethod]
    public async Task ATypeTheDocumentNamesAndNothingRegistered_IsRefused()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ReadAsync("{\"$type\": \"No.Such.Type\"}", typeof(System.Text.StringBuilder)));
    }

    // ── 容器 ─────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AMapWhoseDeclaredTypeHasNoValueTypeArgument_WritesValuesAsObjects()
    {
        var map = new TripleMap<string, int, bool> { ["a"] = 1 };
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);

        await VeloxJsonSerializer.WriteValueAsync(writer, map, typeof(TripleMap<string, int, bool>));
        await writer.CompleteAsync();

        Assert.AreEqual("{\"$id\":\"1\",\"a\":1}", text.ToString());
    }

    [TestMethod]
    public async Task AMapThatWasAlreadyWritten_IsNotWrittenAgain()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);

        await VeloxJsonSerializer.WriteValueAsync(writer, map, typeof(Dictionary<string, int>));
        await VeloxJsonSerializer.WriteValueAsync(writer, map, typeof(Dictionary<string, int>));
        await writer.CompleteAsync();

        StringAssert.Contains(text.ToString(), "\"$ref\"", "the second sighting is a reference, not a second copy");
    }

    [TestMethod]
    public async Task AMapIsWrittenThroughEveryKindOfMemberItCanCarry()
    {
        var loose = new LooseMap();
        loose.AddRaw("a bare token, not an entry");
        loose.AddEntry(null, "skipped");
        loose.AddEntry(new NamelessKey(), 3);

        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);
        await VeloxJsonSerializer.WriteMapAsync(writer, loose, typeof(LooseMap), typeof(object), interfaceKeys: false);
        await writer.CompleteAsync();

        Assert.AreEqual("{\"$id\":\"1\",\"\":3}", text.ToString(), "a key with no name writes an empty member name");
    }

    [TestMethod]
    public async Task AnInterfaceKeyedMap_IsWrittenWithItsKeysReferenceIds()
    {
        var loose = new LooseMap();
        loose.AddEntry(new object(), 1);

        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);
        await VeloxJsonSerializer.WriteMapAsync(writer, loose, typeof(LooseMap), typeof(object), interfaceKeys: true);
        await writer.CompleteAsync();

        Assert.AreEqual("{\"1\":1}", text.ToString());
    }

    [TestMethod]
    public async Task AReadMap_ReplacesWhatTheTargetHeldRatherThanMerging()
    {
        var target = new Dictionary<int, string> { [1] = "old", [9] = "stale" };

        await VeloxJsonSerializer.ReadMapAsync(new VeloxJsonReader("{\"1\": \"new\", \"2\": \"two\"}"), target, typeof(int), typeof(string), false);

        Assert.AreEqual("new", target[1]);
        Assert.AreEqual("two", target[2]);
        Assert.IsFalse(target.ContainsKey(9), "the initializer's keys the document never mentions are gone");
    }

    [TestMethod]
    public async Task AMapWithAnUnresolvableInterfaceKey_SkipsTheEntry()
    {
        var target = new Hashtable();

        await VeloxJsonSerializer.ReadMapAsync(new VeloxJsonReader("{\"7\": 1}"), target, typeof(object), typeof(int), interfaceKeys: true);

        Assert.AreEqual(0, target.Count);
    }

    [TestMethod]
    public async Task ANestedContainer_IsCreatedWhenThereIsNothingToFill()
    {
        VeloxJsonRegistry.RegisterContainerFactory(typeof(CustomList), () => new CustomList());
        VeloxJsonRegistry.RegisterContainerFactory(typeof(CustomMap), () => new CustomMap());

        var list = (CustomList)(await ReadAsync("[1, 2, 3]", typeof(CustomList)))!;
        CollectionAssert.AreEqual(new object?[] { 1L, 2L, 3L }, list);

        var map = (CustomMap)(await ReadAsync("{\"a\": 1}", typeof(CustomMap)))!;
        Assert.AreEqual(1L, map["a"]);

        var existing = new CustomList();
        Assert.AreSame(
            existing,
            await VeloxJsonSerializer.ReadValueAsync(new VeloxJsonReader("[4]"), typeof(CustomList), existing),
            "the instance the member already held is filled");
    }

    // ── 测试替身 ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A type outside the closed world: no generated entry, no container shape.</summary>
    private sealed class NotSerializable
    {
        public int X { get; set; }
    }

    private sealed class TripleMap<TKey, TValue, TExtra> : Dictionary<TKey, TValue> where TKey : notnull;

    private sealed class CustomList : List<object?>;

    private sealed class CustomMap : Hashtable;

    private sealed class NamelessKey
    {
        public override string? ToString() => null;
    }

    private sealed class LooseMap : Hashtable
    {
        private readonly List<object> _raw = [];

        public void AddRaw(object item) => _raw.Add(item);

        public void AddEntry(object? key, object? value) => _raw.Add(new DictionaryEntry(key!, value!));

        public override IDictionaryEnumerator GetEnumerator() => new RawEnumerator(_raw);

        private sealed class RawEnumerator(List<object> items) : IDictionaryEnumerator
        {
            private int _index = -1;

            public object? Current => items[_index];

            public DictionaryEntry Entry => (DictionaryEntry)items[_index]!;

            public object Key => Entry.Key!;

            public object Value => Entry.Value!;

            public bool MoveNext() => ++_index < items.Count;

            public void Reset() => _index = -1;
        }
    }
}
