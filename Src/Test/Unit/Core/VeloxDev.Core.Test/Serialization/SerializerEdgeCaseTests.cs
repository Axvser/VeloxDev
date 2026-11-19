using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The serializer's dispatch corners: the type-known-at-run-time scalar reads, the container shapes the
/// generator never registered, and the guards on the public entry points.
/// </summary>
/// <remarks>
/// These paths are reached from a document only when a member's declared type is wide enough that the generated
/// reader steps aside — a collection element, a map value, an <see cref="object"/> member. Calling the
/// serializer's own surface directly is the shortest way to put each one under test.
/// </remarks>
[TestClass]
public class SerializerEdgeCaseTests
{
    private static object? Read(string document, Type declaredType)
        => VeloxJsonSerializer.ReadValue(new VeloxJsonReader(document), declaredType, null);

    private static string Write(object? value, Type? declaredType)
    {
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false);
        VeloxJsonSerializer.WriteValue(writer, value, declaredType);
        return text.ToString();
    }

    // ── 守卫 ─────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheWriteEntryPoints_RefuseWhatTheyMust()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Serialize(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.WriteTo((TextWriter)null!, new object()));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.WriteTo(new StringWriter(), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.WriteTo((Stream)null!, new object()));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.WriteTo(new MemoryStream(), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.WriteValue(null!, new object(), null));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteMap(null!, new Hashtable(), typeof(Hashtable), typeof(object), false));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteMap(new VeloxJsonWriter(new StringWriter()), null!, typeof(Hashtable), typeof(object), false));
    }

    [TestMethod]
    public void TheReadEntryPoints_RefuseWhatTheyMust()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize<object>((string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize((string)null!, typeof(object)));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize("{}", (Type)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize<object>((TextReader)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize((TextReader)null!, typeof(object)));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize(new StringReader("{}"), (Type)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize<object>((Stream)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize((Stream)null!, typeof(object)));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize(new MemoryStream(), (Type)null!));

        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.ReadUnknown(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.ReadValue(null!, typeof(int), null));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.ReadValue(new VeloxJsonReader("1"), null!, null));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.ReadArray(null!, new List<int>(), typeof(int)));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.ReadArray(new VeloxJsonReader("[]"), null!, typeof(int)));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadMap(null!, new Hashtable(), typeof(string), typeof(int), false));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadMap(new VeloxJsonReader("{}"), null!, typeof(string), typeof(int), false));
    }

    // ── 运行期才知道的标量 ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryScalarPrimitive_ReadsByItsDeclaredType()
    {
        Assert.AreEqual("text", Read("\"text\"", typeof(string)));
        Assert.AreEqual(true, Read("true", typeof(bool)));
        Assert.AreEqual(1, Read("1", typeof(int)));
        Assert.AreEqual(2L, Read("2", typeof(long)));
        Assert.AreEqual(1.5, Read("1.5", typeof(double)));
        Assert.AreEqual(2.5f, Read("2.5", typeof(float)));
        Assert.AreEqual(3.5m, Read("3.5", typeof(decimal)));
        Assert.AreEqual((byte)4, Read("4", typeof(byte)));
        Assert.AreEqual((short)5, Read("5", typeof(short)));
        Assert.AreEqual('x', Read("\"x\"", typeof(char)));
        Assert.AreEqual(Guid.Empty, Read("\"00000000-0000-0000-0000-000000000000\"", typeof(Guid)));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 250 }, (byte[])Read("\"AQL6\"", typeof(byte[]))!);

        // 可空的那一层由 Nullable.GetUnderlyingType 剥掉，读法不变。
        Assert.AreEqual(1, Read("1", typeof(int?)));
    }

    [TestMethod]
    public void TheTwoValuesWhoseKindIsPartOfTheValue_RoundTripThroughTheRuntimeRead()
    {
        var moment = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        var readMoment = (DateTime)Read("\"2026-10-04T12:00:00.0000000Z\"", typeof(DateTime))!;
        Assert.AreEqual(moment, readMoment);
        Assert.AreEqual(DateTimeKind.Utc, readMoment.Kind, "RoundtripKind keeps the instant, not just the digits");

        Assert.AreEqual(TimeSpan.FromMinutes(45), Read("\"00:45:00\"", typeof(TimeSpan)));
    }

    [TestMethod]
    public void AnEnumDeclaredType_HasNoRuntimeRead()
    {
        // 把数字变回枚举成员要枚举的元数据，运行期这条路刻意不做 —— 生成代码用转型。
        Assert.ThrowsExactly<InvalidOperationException>(() => Read("{}", typeof(DayOfWeek)));
    }

    [TestMethod]
    public void ReadUnknown_TakesTheTokenAtItsWord()
    {
        Assert.IsNull(VeloxJsonSerializer.ReadUnknown(new VeloxJsonReader("null")));
        Assert.AreEqual("text", VeloxJsonSerializer.ReadUnknown(new VeloxJsonReader("\"text\"")));
        Assert.AreEqual(true, VeloxJsonSerializer.ReadUnknown(new VeloxJsonReader("true")));
        Assert.AreEqual(12L, VeloxJsonSerializer.ReadUnknown(new VeloxJsonReader("12")));

        var list = (List<object?>)VeloxJsonSerializer.ReadUnknown(new VeloxJsonReader("[1, \"two\"]"))!;
        CollectionAssert.AreEqual(new object?[] { 1L, "two" }, list);
    }

    // ── 容器的形状由声明类型推 ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnArrayWhoseDeclaredTypeIsAnArray_ResolvesItsElementType()
        => Assert.AreEqual("[1,2]", Write(new[] { 1, 2 }, typeof(int[])));

    [TestMethod]
    public void AMapWhoseDeclaredTypeHasNoValueTypeArgument_WritesValuesAsObjects()
    {
        // 三个类型实参的字典：ValueTypeOf 只认正好两个，于是值按 object 写。
        var map = new TripleMap<string, int, bool> { ["a"] = 1 };

        Assert.AreEqual("{\"$id\":\"1\",\"a\":1}", Write(map, typeof(TripleMap<string, int, bool>)));
    }

    [TestMethod]
    public void AMapIsWrittenThroughEveryKindOfMemberItCanCarry()
    {
        // 枚举键、裸对象键、以及一个 ToString 返回 null 的键 —— 后者写出来是空成员名。
        var writer = new StringWriter();
        var loose = new LooseMap();
        loose.AddRaw("a bare token, not an entry");
        loose.AddEntry(null, "skipped");
        loose.AddEntry(new NamelessKey(), 3);

        VeloxJsonSerializer.WriteMap(new VeloxJsonWriter(writer, indented: false), loose, typeof(LooseMap), typeof(object), interfaceKeys: false);

        Assert.AreEqual("{\"$id\":\"1\",\"\":3}", writer.ToString(), "the null-named key writes an empty name");
    }

    [TestMethod]
    public void AnInterfaceKeyedMap_IsWrittenWithItsKeysReferenceIds()
    {
        var loose = new LooseMap();
        loose.AddEntry(new object(), 1);

        var writer = new StringWriter();
        VeloxJsonSerializer.WriteMap(new VeloxJsonWriter(writer, indented: false), loose, typeof(LooseMap), typeof(object), interfaceKeys: true);

        Assert.AreEqual("{\"1\":1}", writer.ToString(), "the key's fresh id is the member name, and the map itself carries no $id");
    }

    [TestMethod]
    public void AMapWithAnUnresolvableInterfaceKey_SkipsTheEntry()
    {
        var target = new Hashtable();

        VeloxJsonSerializer.ReadMap(new VeloxJsonReader("{\"7\": 1, \"8\": 2}"), target, typeof(object), typeof(int), interfaceKeys: true);

        Assert.AreEqual(0, target.Count, "an id nothing registered leaves nothing to key the entry by");
    }

    [TestMethod]
    public void AReadMap_ReplacesWhatTheTargetHeldRatherThanMerging()
    {
        var target = new Dictionary<int, string> { [1] = "old", [9] = "stale" };

        VeloxJsonSerializer.ReadMap(new VeloxJsonReader("{\"1\": \"new\", \"2\": \"two\"}"), target, typeof(int), typeof(string), interfaceKeys: false);

        Assert.AreEqual("new", target[1], "a key the document has is overwritten");
        Assert.AreEqual("two", target[2], "and a key it does not had no business surviving");
        Assert.IsFalse(target.ContainsKey(9), "the initializer's keys the document never mentions are gone");
    }

    [TestMethod]
    public void AMapWithEnumKeys_ReadsThemByName()
    {
        var target = new Dictionary<DayOfWeek, int>();

        VeloxJsonSerializer.ReadMap(new VeloxJsonReader("{\"Monday\": 1}"), target, typeof(DayOfWeek), typeof(int), false);

        Assert.AreEqual(1, target[DayOfWeek.Monday], "an enum key was written as its name and reads back the same");
    }

    [TestMethod]
    public void ANestedContainer_IsCreatedWhenThereIsNothingToFill()
    {
        // 没有工厂就造不出泛型容器（那要反射），所以生成器为嵌套形状登记了构造方式 —— 这里登记两种：
        // 一个非泛型的列表（元素类型只能落到 object）与一个非泛型的字典（键与值同理）。
        VeloxJsonRegistry.RegisterContainerFactory(typeof(CustomList), () => new CustomList());
        VeloxJsonRegistry.RegisterContainerFactory(typeof(CustomMap), () => new CustomMap());

        var list = (CustomList)Read("[1, 2, 3]", typeof(CustomList))!;
        CollectionAssert.AreEqual(new object?[] { 1L, 2L, 3L }, list);

        var map = (CustomMap)Read("{\"a\": 1}", typeof(CustomMap))!;
        Assert.AreEqual(1L, map["a"]);

        // 已经有一个合用的实例时就地填，而不是另造一个。
        var existing = new CustomList();
        Assert.AreSame(existing, Read("[4]", typeof(CustomList), existing), "the instance the member already held is filled");
    }

    private static object? Read(string document, Type declaredType, object? existing)
        => VeloxJsonSerializer.ReadValue(new VeloxJsonReader(document), declaredType, existing);

    // ── 测试替身 ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A dictionary whose value type argument is not the only one — three type parameters, not two.</summary>
    private sealed class TripleMap<TKey, TValue, TExtra> : Dictionary<TKey, TValue> where TKey : notnull;

    /// <summary>A list that is not generic, so its element type can only fall back to <see cref="object"/>.</summary>
    private sealed class CustomList : List<object?>;

    /// <summary>A map that is not generic, so its key and value types can only fall back to <see cref="object"/>.</summary>
    private sealed class CustomMap : Hashtable;

    /// <summary>A key whose name is nothing at all.</summary>
    private sealed class NamelessKey
    {
        public override string? ToString() => null;
    }

    /// <summary>A dictionary that also hands out raw things that are not entries — what a hostile enumerator does.</summary>
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
