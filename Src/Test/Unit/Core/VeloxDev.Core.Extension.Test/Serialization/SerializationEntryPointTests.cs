using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The public read entries, held to each other.
/// </summary>
/// <remarks>
/// <para>
/// The suite walked the string entries and the <c>Type</c> overload of the <c>TextReader</c> one; the four
/// source-shaped entries a caller actually uses — <c>TextReader</c> and <c>Stream</c>, generic and by type —
/// had no test of their own, and neither did the <see cref="ArgumentNullException"/> each of them promises.
/// </para>
/// <para>
/// A stream also carries the one thing a string cannot: a byte order mark. The reader is built with
/// <c>detectEncodingFromByteOrderMarks</c>, so a trimmed document and a marked one must both parse.
/// </para>
/// </remarks>
[TestClass]
public partial class SerializationEntryPointTests
{
    internal sealed partial class EntryModel
    {
        [VeloxProperty] private int count;
        [VeloxProperty] private string? name;
    }

    private static EntryModel Sample() => new() { Count = 7, Name = "seven" };

    private static string Document() => VeloxJsonSerializer.Serialize(Sample());

    private static void AssertIsTheSample(EntryModel? restored, string entry)
    {
        Assert.IsNotNull(restored, entry + ": read nothing");
        Assert.AreEqual(7, restored.Count, entry + ": the number changed");
        Assert.AreEqual("seven", restored.Name, entry + ": the text changed");
    }

    private static Stream Bytes(string document, bool withByteOrderMark)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: withByteOrderMark);
        return new MemoryStream(encoding.GetPreamble().Concat(encoding.GetBytes(document)).ToArray());
    }

    [TestMethod]
    public void ATextReaderEntry_ReadsWhatTheStringEntryReads()
    {
        AssertIsTheSample(
            VeloxJsonSerializer.Deserialize<EntryModel>(new StringReader(Document())), "TextReader<T>");
    }

    [TestMethod]
    public void AStreamEntry_ReadsWhatTheStringEntryReads()
    {
        using var stream = Bytes(Document(), withByteOrderMark: false);
        AssertIsTheSample(VeloxJsonSerializer.Deserialize<EntryModel>(stream), "Stream<T>");
    }

    [TestMethod]
    public void AStreamWithAByteOrderMark_ReadsTheSameDocument()
    {
        using var stream = Bytes(Document(), withByteOrderMark: true);
        AssertIsTheSample(VeloxJsonSerializer.Deserialize<EntryModel>(stream), "Stream<T> with a mark");
    }

    [TestMethod]
    public void TheTypeOverloads_AgreeWithTheirGenericTwins()
    {
        // 四对入口读同一份文档必须给同一个图 —— 否则调用方要为入口各写一条分支。
        var expected = VeloxJsonSerializer.Serialize(Sample());

        var byType = VeloxJsonSerializer.Deserialize(new StringReader(expected), typeof(EntryModel));
        Assert.AreEqual(expected, VeloxJsonSerializer.Serialize(byType!), "Deserialize(TextReader, Type)");

        using var stream2 = Bytes(expected, withByteOrderMark: false);
        var byTypeStream = VeloxJsonSerializer.Deserialize(stream2, typeof(EntryModel));
        Assert.AreEqual(expected, VeloxJsonSerializer.Serialize(byTypeStream!), "Deserialize(Stream, Type)");
    }

    [TestMethod]
    public async Task TheAsyncEntries_ReadWhatTheSyncOneReads()
    {
        var expected = Document();

        AssertIsTheSample(await VeloxJsonSerializer.DeserializeAsync<EntryModel>(new StringReader(expected)), "async TextReader<T>");

        using var stream = Bytes(expected, withByteOrderMark: true);
        AssertIsTheSample(await VeloxJsonSerializer.DeserializeAsync<EntryModel>(stream), "async Stream<T>");
    }

    [TestMethod]
    public void EveryEntry_RefusesANullSource()
    {
        // 每条入口都承诺过 ArgumentNullException；漏一条，调用方就得靠空引用异常去猜。
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize<EntryModel>((TextReader)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize<EntryModel>((Stream)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize((TextReader)null!, typeof(EntryModel)));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize((Stream)null!, typeof(EntryModel)));

        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize((TextReader)new StringReader(Document()), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => VeloxJsonSerializer.Deserialize((Stream)new MemoryStream(), null!));
    }

    [TestMethod]
    public async Task EveryAsyncEntry_RefusesANullSource()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync<EntryModel>((TextReader)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync<EntryModel>((Stream)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync((TextReader)null!, typeof(EntryModel)));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync((Stream)null!, typeof(EntryModel)));
    }
}
