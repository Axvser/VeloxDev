using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The convenience surface over the engine: the extension methods and their fluent options.
/// </summary>
/// <remarks>
/// Twenty-odd public entry points had no test of their own — the suite went to the engine underneath them. What
/// this layer adds is <see cref="SerializationOptions"/> and the failure behaviour: a document that cannot be
/// read is a <see langword="false"/> from <c>TryDeserialize</c> and an exception from <c>Deserialize</c>, and the
/// two must not be confused.
/// </remarks>
[TestClass]
public partial class ViewModelSerializerTests
{
    internal sealed partial class OptionModel
    {
        [VeloxProperty] private int count;
        [VeloxProperty] private string? name;

        /// <summary>Declared type the exclusion test drops by name.</summary>
        [VeloxProperty] private Nested? nested;
    }

    internal sealed partial class Nested
    {
        [VeloxProperty] private int inner;
    }

    private static OptionModel Sample() => new()
    {
        Count = 3,
        Name = "three",
        Nested = new Nested { Inner = 9 },
    };

    [TestMethod]
    public void ARoundTrip_ThroughTheConvenienceSurface()
    {
        var restored = VeloxJsonSerializer.Serialize(Sample()).Deserialize<OptionModel>();

        Assert.AreEqual(3, restored.Count);
        Assert.AreEqual("three", restored.Name);
        Assert.AreEqual(9, restored.Nested!.Inner);
    }

    [TestMethod]
    public void CompactAndIndented_DifferOnlyInWhitespace()
    {
        var indented = Sample().Serialize();
        var compact = Sample().Serialize(SerializationOptions.Create().WithCompact());

        StringAssert.Contains(indented, Environment.NewLine, "the default is human-readable");
        Assert.IsFalse(compact.Contains(Environment.NewLine, StringComparison.Ordinal), "compact has no line breaks");

        // 缩进版还有换行与冒号后的空格，所以「只差空白」要比的是**去掉全部空白**之后的那份。
        Assert.AreEqual(Squash(indented), Squash(compact), "and nothing else changed");

        static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    [TestMethod]
    public void AnExcludedPropertyType_DropsEveryMemberOfIt()
    {
        // 这条选项存在的原因：把编译过的图写出来时不拖着它来自的那棵树（节点的 Parent 是可写成员，
        // 引用图会从任一节点够到整份文档）。
        var document = Sample().Serialize(SerializationOptions.Create().WithExcludedPropertyTypes(typeof(Nested)));

        Assert.IsFalse(document.Contains("\"Nested\"", StringComparison.Ordinal), "the excluded member is gone");
        Assert.IsTrue(document.Contains("\"Count\"", StringComparison.Ordinal), "and its siblings are not");
    }

    [TestMethod]
    public void TryDeserialize_AnswersFalseInsteadOfThrowing()
    {
        Assert.IsTrue("{ \"$id\": \"1\", \"$type\": \"x\", \"Count\": 1 }".TryDeserialize<OptionModel>(out var good), "a good document");
        Assert.AreEqual(1, good!.Count);

        Assert.IsFalse("not json at all".TryDeserialize<OptionModel>(out var bad), "a broken document is a false");
        Assert.IsNull(bad, "and nothing comes back with it");
    }

    [TestMethod]
    public void DeserializingNothing_IsRefused()
    {
        // 引擎给 null（文档是 JSON 字面量）时，这条入口选择抛出去而不是交回一个 null。
        Assert.ThrowsExactly<InvalidOperationException>(() => "null".Deserialize<OptionModel>());
    }

    [TestMethod]
    public void SerializingNothing_IsRefused()
    {
        OptionModel? nothing = null;

        Assert.ThrowsExactly<ArgumentNullException>(() => nothing!.Serialize());
    }

    [TestMethod]
    public async Task TheAsyncTwins_AgreeWithTheSyncOnes()
    {
        var expected = Sample().Serialize();

        Assert.AreEqual(expected, await Sample().SerializeAsync());

        var restored = await expected.DeserializeAsync<OptionModel>();
        Assert.AreEqual(3, restored.Count);
    }

    [TestMethod]
    public void TheWriterAndStreamEntries_AgreeWithTheStringOnes()
    {
        var expected = Sample().Serialize();

        using var text = new StringWriter();
        Sample().SerializeToTextWriter(text);
        Assert.AreEqual(expected, text.ToString(), "SerializeToTextWriter");

        using var stream = new MemoryStream();
        Sample().SerializeToStream(stream);
        stream.Position = 0;
        Assert.AreEqual(expected, Encoding.UTF8.GetString(stream.ToArray()), "SerializeToStream");

        Assert.AreEqual(expected, Encoding.UTF8.GetString(Sample().SerializeToUtf8Bytes()), "SerializeToUtf8Bytes");

        Assert.AreEqual("three", new StringReader(expected).DeserializeFromTextReader<OptionModel>()!.Name, "DeserializeFromTextReader");
        Assert.AreEqual("three", new MemoryStream(Encoding.UTF8.GetBytes(expected)).DeserializeFromStream<OptionModel>()!.Name, "DeserializeFromStream");
    }

    [TestMethod]
    public async Task TheByteEntry_HasAnAsyncTwin()
    {
        var expected = Sample().Serialize();
        Assert.AreEqual(expected, Encoding.UTF8.GetString(await Sample().SerializeToUtf8BytesAsync()));
    }

    [TestMethod]
    public void TheOptionsOverloads_CarryTheOptionsThrough()
    {
        var compact = Sample().Serialize(SerializationOptions.Create().WithCompact());
        Assert.IsFalse(compact.Contains(Environment.NewLine, StringComparison.Ordinal), "Serialize with options");

        Assert.AreEqual(3, compact.Deserialize<OptionModel>(SerializationOptions.Create().WithCompact()).Count, "Deserialize with options");

        Assert.IsTrue(compact.TryDeserialize<OptionModel>(SerializationOptions.Create().WithIndented(), out var tried), "TryDeserialize with options");
        Assert.AreEqual(3, tried!.Count);
    }

    [TestMethod]
    public async Task TheAsyncOptionsOverloads_CarryTheOptionsThrough()
    {
        var compact = await Sample().SerializeAsync(SerializationOptions.Create().WithCompact());
        Assert.IsFalse(compact.Contains(Environment.NewLine, StringComparison.Ordinal), "SerializeAsync with options");

        var restored = await compact.DeserializeAsync<OptionModel>(SerializationOptions.Create().WithCompact());
        Assert.AreEqual(3, restored.Count, "DeserializeAsync with options");
    }

    [TestMethod]
    public void TheWriterAndByteEntries_TakeTheOptionsToo()
    {
        var compact = Sample().Serialize(SerializationOptions.Create().WithCompact());
        var options = SerializationOptions.Create().WithCompact();

        using var text = new StringWriter();
        Sample().SerializeToTextWriter(text, options);
        Assert.AreEqual(compact, text.ToString(), "SerializeToTextWriter with options");

        using var stream = new MemoryStream();
        Sample().SerializeToStream(stream, options);
        Assert.AreEqual(compact, Encoding.UTF8.GetString(stream.ToArray()), "SerializeToStream with options");

        Assert.AreEqual(compact, Encoding.UTF8.GetString(Sample().SerializeToUtf8Bytes(options)), "SerializeToUtf8Bytes with options");
    }

    [TestMethod]
    public void AValueTree_ReadsIntoAType()
    {
        var tree = VeloxJsonValue.Parse(Sample().Serialize());

        var restored = (OptionModel)tree.DeserializeToType(typeof(OptionModel))!;

        Assert.AreEqual(3, restored.Count);
        Assert.AreEqual(9, restored.Nested!.Inner);
    }
}
