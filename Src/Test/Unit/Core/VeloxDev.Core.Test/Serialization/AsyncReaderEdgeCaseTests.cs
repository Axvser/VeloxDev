using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The asynchronous reader's corners, one per synchronous test in <see cref="ReaderEdgeCaseTests"/>.
/// </summary>
/// <remarks>
/// The two faces are handwritten copies of each other. A test on one face says nothing about the other: the
/// escapes, the refills and the error paths are separate code, and the 2026-10-05 round of coverage work is
/// exactly where four of them turned out to disagree.
/// </remarks>
[TestClass]
public class AsyncReaderEdgeCaseTests
{
    private static VeloxJsonReader Window(string document, int chunk = 8) => new(new StringReader(document), chunk);

    private static VeloxJsonReader Text(string document) => new(document);

    // ── 字符串入口走异步面 ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TheStringEntry_AnswersTheAsyncCallsToo()
    {
        // 文档整份在内存里时异步面不续读，但它仍然是一条独立的读取链 —— 含转义的字符串走的是它自己
        // 那条「先扫、再在缓冲区上补」的分支。
        Assert.AreEqual("a\nb", await Text("\"a\\nb\"").ReadStringAsync(), "an escape on the string entry");
        Assert.AreEqual("plain", await Text("\"plain\"").ReadStringAsync(), "and the clean fast path");

        var reader = Text("12");
        Assert.AreEqual(12, await reader.ReadInt32Async());
        Assert.IsTrue(await reader.AtEndAsync(), "the whole document was consumed");
    }

    [TestMethod]
    public async Task TheGuards_RefuseWhatTheyMust()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync<object>((TextReader)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync<object>((Stream)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync(new StringReader("{}"), (Type)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.DeserializeAsync(new MemoryStream(), (Type)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteToAsync((TextWriter)null!, new object()));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteToAsync(new StringWriter(), null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteToAsync((Stream)null!, new object()));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.WriteToAsync(new MemoryStream(), null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadValueAsync(null!, typeof(int), null));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => VeloxJsonSerializer.ReadValueAsync(new VeloxJsonReader("1"), null!, null));

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Text("{}").ReadEnumAsync(null!));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Window("{}").ReadMemberNameAsync());
    }

    // ── 手写文档：探测回退与元数据 ────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AReferenceThatCarriesMore_IsNotAReference()
    {
        var (opened, referenceId, typeName) = await Window("{\"$ref\": \"1\", \"x\": 2}").BeginObjectAsync();

        Assert.IsTrue(opened, "it opens as a plain object");
        Assert.AreEqual(0, referenceId);
        Assert.IsNull(typeName);
    }

    [TestMethod]
    public async Task AnUnquotedTypeName_ReadsAsAToken()
    {
        var (opened, referenceId, typeName) = await Window("{\"$id\": \"1\", \"$type\": Some.Bare.Name}").BeginObjectAsync();

        Assert.IsTrue(opened);
        Assert.AreEqual(1, referenceId);
        Assert.AreEqual("Some.Bare.Name", typeName);
    }

    [TestMethod]
    public async Task AReferenceIdThatIsNotAQuotedNumber_IsRefused()
    {
        await Assert.ThrowsExactlyAsync<FormatException>(
            () => Window("{\"$id\": \"12").BeginObjectAsync());
        await Assert.ThrowsExactlyAsync<FormatException>(
            () => Window("{\"$id\": \"12x}").BeginObjectAsync());
        await Assert.ThrowsExactlyAsync<FormatException>(
            () => Window("{\"$id\": \"x\"}").BeginObjectAsync());
    }

    [TestMethod]
    public async Task AMetadataNameThatIsNotQuoted_StillReadsTheObject()
    {
        // 探测的名字不是 `$id`/`$type`/`$ref`，而且连引号都没有 —— StartsWithQuoted 的第一道判断就退回来。
        var (opened, referenceId, typeName) = await Window("{123456:2}").BeginObjectAsync();

        Assert.IsTrue(opened);
        Assert.AreEqual(0, referenceId);
        Assert.IsNull(typeName);
    }

    // ── 手写文档：tokenizer ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AStringThatRunsOffTheEnd_IsRefused()
    {
        await Assert.ThrowsExactlyAsync<FormatException>(() => Window("\"abc").ReadStringAsync());
        await Assert.ThrowsExactlyAsync<FormatException>(() => Window("\"a\\n").ReadStringAsync());
        await Assert.ThrowsExactlyAsync<FormatException>(() => Window("\"a\\").ReadStringAsync());
        await Assert.ThrowsExactlyAsync<FormatException>(() => Window("\"\\u00").ReadStringAsync());
    }

    [TestMethod]
    public async Task AMalformedBoolean_IsRefused()
    {
        // 长度不够与真的不是布尔，两条读法都试过再失败。
        await Assert.ThrowsExactlyAsync<FormatException>(() => Window("tru").ReadBooleanAsync());
        await Assert.ThrowsExactlyAsync<FormatException>(() => Window("maybe").ReadBooleanAsync());
    }

    [TestMethod]
    public async Task AnEmptyBareToken_IsRefused()
        => await Assert.ThrowsExactlyAsync<FormatException>(() => Window("}").ReadTextAsync());

    [TestMethod]
    public async Task AReaderPastItsWhitespace_IsRefused()
    {
        // 空白之后什么也没有：PeekAsync 在这里必须出声。
        await Assert.ThrowsExactlyAsync<FormatException>(() => Window("   ").ReadTextAsync());

        var atEnd = Window("   ");
        Assert.IsTrue(await atEnd.AtEndAsync(), "and AtEnd is the one call that answers instead of throwing");
    }

    [TestMethod]
    public async Task AMemberNameThatNeverCloses_IsRefused()
    {
        await Assert.ThrowsExactlyAsync<FormatException>(async () =>
        {
            var reader = Window("{\"ab");
            await reader.BeginObjectAsync();
            await reader.NextMemberAsync();
        });

        // 转义之后的那个字符也是最后一个。
        await Assert.ThrowsExactlyAsync<FormatException>(async () =>
        {
            var reader = Window("{\"a\\");
            await reader.BeginObjectAsync();
            await reader.NextMemberAsync();
        });
    }

    [TestMethod]
    public async Task SkippingAValue_StepsOverEveryShape()
    {
        // SkipValueAsync 的对象与数组两条分支：既有语料里，未知成员从来没有长成这两种形状。
        var reader = Window("{\"a\": [1, 2], \"b\": {\"c\": 3}, \"d\": \"text\", \"e\": 4}");

        await reader.BeginObjectAsync();
        while (await reader.NextMemberAsync()) await reader.SkipValueAsync();
        await reader.FinishObjectAsync();

        Assert.IsTrue(await reader.AtEndAsync());
    }

    [TestMethod]
    public async Task AReferenceIdReadsWithoutItsQuotesToo()
    {
        var (opened, referenceId, _) = await Window("{\"$id\": 12}").BeginObjectAsync();

        Assert.IsTrue(opened);
        Assert.AreEqual(12, referenceId);
    }

    [TestMethod]
    public async Task EveryScalarPrimitive_ReadsOnTheAsyncFace()
    {
        // 异步面的标量读法与同步面同一套不变区域性拼法。
        var reader = Window("1.5");
        Assert.AreEqual(1.5, await reader.ReadDoubleAsync());
        Assert.AreEqual(1.5m, await Window("1.5").ReadDecimalAsync());
        Assert.AreEqual(1.5f, await Window("1.5").ReadSingleAsync());
        Assert.AreEqual(7L, await Window("7").ReadInt64Async());
    }
}
