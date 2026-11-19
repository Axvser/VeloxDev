using System;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Documents our own writer never produces: every JSON escape, an escaped member name, and the shapes a reader
/// meets only when the text came from somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// A round trip can only reach the escapes the writer chooses to emit — it never writes <c>\/</c>, <c>\b</c> or
/// <c>\f</c> — and it can never escape a member name, because the names it writes are identifiers. So the
/// decoder's remaining arms, the escaped-name fallback in <c>MemberNameEquals</c>, and the branches a malformed
/// document takes are reachable only from text written by hand.
/// </para>
/// <para>
/// Each document starts as the writer's own output (so the envelope is right) and gets one token replaced —
/// the replacement is the thing under test.
/// </para>
/// </remarks>
[TestClass]
public partial class HandWrittenDocumentTests
{
    internal sealed partial class ReaderModel
    {
        [VeloxProperty] private string? text;
        [VeloxProperty] private bool flag;
        [VeloxProperty] private int count;
    }

    private static string Document() => VeloxJsonSerializer.Serialize(new ReaderModel
    {
        Text = "plain",
        Flag = true,
        Count = 1,
    });

    private static string Replace(string document, string from, string to)
    {
        var patched = document.Replace(from, to, StringComparison.Ordinal);
        Assert.AreNotEqual(document, patched, "the document did not contain " + from);
        return patched;
    }

    [TestMethod]
    public void EveryEscapeInTheLanguage_IsDecoded()
    {
        // 写入器只产出它自己会写的那些，所以 `\/` `\b` `\f` 这些只有手写文档才走得到。
        var document = Replace(Document(), "\"Text\": \"plain\"", "\"Text\": \"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0041\"");

        var restored = VeloxJsonSerializer.Deserialize<ReaderModel>(document)!;

        Assert.AreEqual("\"\\/\b\f\n\r\tA", restored.Text);
    }

    [TestMethod]
    public async Task EveryEscapeInTheLanguage_IsDecodedOnTheAsyncFace()
    {
        var document = Replace(Document(), "\"Text\": \"plain\"", "\"Text\": \"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0041\"");

        var restored = await VeloxJsonSerializer.DeserializeAsync<ReaderModel>(new ChunkedTextReader(document, 1));

        Assert.AreEqual("\"\\/\b\f\n\r\tA", restored!.Text);
    }

    [TestMethod]
    public void AnEscapedMemberName_IsDecodedBeforeItIsDispatched()
    {
        // 名字写成 `\u0043ount` —— `MemberNameEquals` 见名字带转义才会回退去解码，这条把它走一遍。
        var document = Replace(Document(), "\"Count\":", "\"\\u0043ount\":");

        var restored = VeloxJsonSerializer.Deserialize<ReaderModel>(document)!;

        Assert.AreEqual(1, restored.Count, "the escaped name must still reach its member");
    }

    [TestMethod]
    public void AMalformedEscape_IsRefused()
    {
        var notHex = Replace(Document(), "\"Text\": \"plain\"", "\"Text\": \"\\u00ZZ\"");
        Assert.ThrowsExactly<FormatException>(() => VeloxJsonSerializer.Deserialize<ReaderModel>(notHex));

        var unknown = Replace(Document(), "\"Text\": \"plain\"", "\"Text\": \"\\q\"");
        Assert.ThrowsExactly<FormatException>(() => VeloxJsonSerializer.Deserialize<ReaderModel>(unknown));
    }

    [TestMethod]
    public void AValueThatIsNotWhatItClaims_IsRefused()
    {
        var notABoolean = Replace(Document(), "\"Flag\": true", "\"Flag\": maybe");
        Assert.ThrowsExactly<FormatException>(() => VeloxJsonSerializer.Deserialize<ReaderModel>(notABoolean));
    }

    [TestMethod]
    public void AnUnknownMemberOfEveryShape_IsSkipped()
    {
        // `SkipValue` 的四个分支：对象、数组、带引号的字符串、裸 token。这四个成员模型里都没有，
        // 所以生成代码会跳过它们 —— 走的正是那四条。
        const string extra =
            "\"AnObject\": { \"inner\": [1, { \"deeper\": \"text\" }] }, " +
            "\"AnArray\": [1, \"two\", { \"three\": 3 }, [4]], " +
            "\"AString\": \"skipped\", " +
            "\"ABareToken\": 12345, " +
            "\"ANull\": null, ";

        var document = Replace(Document(), "\"$id\": \"1\",", extra + "\"$id\": \"1\",");

        var restored = VeloxJsonSerializer.Deserialize<ReaderModel>(document)!;

        Assert.AreEqual(1, restored.Count, "the known members still read after the unknown ones");
        Assert.AreEqual("plain", restored.Text);
    }

    [TestMethod]
    public void ADocumentWhoseMetadataIsNotMetadata_StillReads()
    {
        // 以 `$` 开头但不是 `$id`/`$type` 的成员：探测读过再整体回退，回退位置必须跨压缩保住。
        var document = Replace(Document(), "\"$id\": \"1\",", "\"$other\": \"x\", \"$id\": \"1\",");

        var restored = VeloxJsonSerializer.Deserialize<ReaderModel>(document)!;

        Assert.AreEqual(1, restored.Count);
    }

    [TestMethod]
    public async Task AMalformedEscape_IsRefusedOnTheAsyncFace()
    {
        var notHex = Replace(Document(), "\"Text\": \"plain\"", "\"Text\": \"\\u00ZZ\"");
        await Assert.ThrowsExactlyAsync<FormatException>(
            () => VeloxJsonSerializer.DeserializeAsync<ReaderModel>(new ChunkedTextReader(notHex, 1)));

        var unknown = Replace(Document(), "\"Text\": \"plain\"", "\"Text\": \"\\q\"");
        await Assert.ThrowsExactlyAsync<FormatException>(
            () => VeloxJsonSerializer.DeserializeAsync<ReaderModel>(new ChunkedTextReader(unknown, 1)));
    }

    [TestMethod]
    public async Task ADocumentWhoseMetadataIsNotMetadata_StillReadsOnTheAsyncFace()
    {
        var document = Replace(Document(), "\"$id\": \"1\",", "\"$other\": \"x\", \"$id\": \"1\",");

        var restored = await VeloxJsonSerializer.DeserializeAsync<ReaderModel>(new ChunkedTextReader(document, 1));

        Assert.AreEqual(1, restored!.Count);
    }

    [TestMethod]
    public async Task AReferenceIdWithoutItsQuotes_IsRefused()
    {
        // 引用出现在**成员值**的位置：`$ref` 的 id 必须带引号，不带就是坏文档。
        var malformed = Replace(Document(), "\"Count\": 1", "\"Count\": { \"$ref\": 2 }");

        Assert.ThrowsExactly<FormatException>(() => VeloxJsonSerializer.Deserialize<ReaderModel>(malformed));
        await Assert.ThrowsExactlyAsync<FormatException>(
            () => VeloxJsonSerializer.DeserializeAsync<ReaderModel>(new ChunkedTextReader(malformed, 1)));
    }

    [TestMethod]
    public async Task ThePredicates_AnswerForTheTokenAhead()
    {
        var reader = new VeloxJsonReader("\"text\"");
        Assert.IsTrue(reader.NextIsString(), "a quoted string");
        Assert.IsFalse(reader.NextIsArray());
        Assert.IsFalse(reader.NextIsObject());
        Assert.IsFalse(reader.NextIsNull());

        var async2 = new VeloxJsonReader(new ChunkedTextReader("[1]", 1));
        Assert.IsTrue(await async2.NextIsArrayAsync(), "an array");
        Assert.IsFalse(await async2.NextIsStringAsync());
        Assert.IsFalse(await async2.NextIsObjectAsync());
        Assert.IsFalse(await async2.NextIsNullAsync());

        var nothing = new VeloxJsonReader(new ChunkedTextReader("null", 1));
        Assert.IsTrue(await nothing.NextIsNullAsync(), "the literal");
    }

    [TestMethod]
    public async Task AnEnumWrittenByName_ReadsOnTheAsyncFace()
    {
        // 按名字写出的枚举在异步面有自己的一条读法（`ReadEnumAsync`）—— 同步面由 EnumNameTests 覆盖。
        var model = new EnumNameTests.EnumNameModel
        {
            Named = ShapeRoundTripTests.ScalarKind.Second,
            Numeric = ShapeRoundTripTests.ScalarKind.First,
        };
        var document = VeloxJsonSerializer.Serialize(model);

        var restored = await VeloxJsonSerializer.DeserializeAsync<EnumNameTests.EnumNameModel>(new ChunkedTextReader(document, 1));

        Assert.AreEqual(model.Named, restored!.Named, "the named enum");
        Assert.AreEqual(model.Numeric, restored.Numeric, "and the one that keeps its number");
    }

    [TestMethod]
    public void AtEnd_AnswersOnlyWhenTheDocumentIs()
    {
        var reader = new VeloxJsonReader("1");

        Assert.IsFalse(reader.AtEnd, "a value is still there");

        reader.SkipValue();

        Assert.IsTrue(reader.AtEnd, "and then it is not");
    }
}
