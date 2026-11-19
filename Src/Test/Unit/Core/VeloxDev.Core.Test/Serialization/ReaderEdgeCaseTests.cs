using System;
using System.IO;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The synchronous reader's corners: the guards its public surface promises, and the malformed documents a
/// writer of our own never produces.
/// </summary>
/// <remarks>
/// A round trip can only reach the arms the writer chooses to take. Everything here is either a null guard on
/// public API or a document written by hand to land on a specific fallback — the places where a bug hides
/// precisely because the corpus never goes there.
/// </remarks>
[TestClass]
public class ReaderEdgeCaseTests
{
    private static VeloxJsonReader Window(string document, int chunk = 8) => new(new StringReader(document), chunk);

    // ── 构造与守卫 ────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheConstructors_RefuseWhatTheyMust()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new VeloxJsonReader((string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new VeloxJsonReader((TextReader)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new VeloxJsonReader((Stream)null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VeloxJsonReader(new StringReader("x"), 0));

        Assert.ThrowsExactly<ArgumentException>(() => new VeloxJsonReader(""));
        Assert.ThrowsExactly<ArgumentException>(() => new VeloxJsonReader(new StringReader("")));
    }

    [TestMethod]
    public void MemberNameComparison_RefusesNoName()
        => Assert.ThrowsExactly<ArgumentNullException>(() => new VeloxJsonReader("{}").MemberNameEquals(null!));

    [TestMethod]
    public void ReadEnum_RefusesNoType()
        => Assert.ThrowsExactly<ArgumentNullException>(() => new VeloxJsonReader("{}").ReadEnum(null!));

    [TestMethod]
    public void AnUnregisteredReference_ResolvesToNothing()
        => Assert.IsNull(new VeloxJsonReader("{}").ResolveReference(7), "an id nothing registered is not an error");

    // ── 手写文档：探测回退与元数据 ────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AReferenceThatCarriesMore_IsNotAReference()
    {
        // `$ref` 之外还有成员：探测成功一半，整体回退，于是它按普通对象读，id 为 0。
        var reader = new VeloxJsonReader("{\"$ref\": \"1\", \"x\": 2}");

        Assert.IsTrue(reader.BeginObject(out var id, out var typeName), "it opens as a plain object");
        Assert.AreEqual(0, id, "and carries no reference id");
        Assert.IsNull(typeName);
        Assert.IsTrue(reader.NextMember(out var name), "the member is still there to read");
        Assert.AreEqual("$ref", name);
    }

    [TestMethod]
    public void AnUnquotedTypeName_ReadsAsAToken()
    {
        // `$type` 的裸写法（没有引号）走 ReadBareToken 那一条。
        var reader = new VeloxJsonReader("{\"$id\": \"1\", \"$type\": Some.Bare.Name}");

        Assert.IsTrue(reader.BeginObject(out var id, out var typeName));
        Assert.AreEqual(1, id);
        Assert.AreEqual("Some.Bare.Name", typeName, "a bare type name is taken as it stands");
    }

    [TestMethod]
    public void AReferenceIdThatIsNotAQuotedNumber_IsRefused()
    {
        // 引号没闭合：数字读完之后 Require 失败。
        Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("{\"$id\": \"12").BeginObject(out _, out _));

        // 数字后面跟的不是引号。
        Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("{\"$id\": \"12x}").BeginObject(out _, out _));

        // 完全没有数字。
        Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("{\"$id\": \"x\"}").BeginObject(out _, out _));

        // 裸写法也认：`{"$id": 12}`。
        var bare = new VeloxJsonReader("{\"$id\": 12}");
        Assert.IsTrue(bare.BeginObject(out var id, out _));
        Assert.AreEqual(12, id, "a bare id reads the same as a quoted one");
    }

    // ── 手写文档：tokenizer ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AStringThatRunsOffTheEnd_IsRefused()
    {
        // string 入口的快路径扫到末尾都没有结束引号。
        Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("\"abc").ReadString());

        // 转义之后的两种情况：转义字符本身是最后一个（缺引号收尾），以及转义被截断。
        Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("\"a\\n").ReadString());
        Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("\"a\\").ReadString());
    }

    [TestMethod]
    public void EveryHexDigitCase_IsDecoded()
    {
        var reader = new VeloxJsonReader("\"\\u00e5\\u00C5\"");

        Assert.AreEqual("åÅ", reader.ReadString(), "lower and upper hex digits both count");
    }

    [TestMethod]
    public void ANonHexadecimalEscape_IsRefused()
        => Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("\"\\u00ZZ\"").ReadString());

    [TestMethod]
    public void AMalformedBoolean_IsRefused()
        // 只有三个字符：长度不够，两个字面量都不匹配。
        => Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("tru").ReadBoolean());

    [TestMethod]
    public void AnEmptyBareToken_IsRefused()
        // 光标正好落在分隔符上：裸 token 一个字符都取不到。
        => Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("}").ReadText());

    [TestMethod]
    public void AReaderAtTheEndOfItsWhitespace_IsRefused()
        // 空白之后什么也没有 —— Peek 在这里必须出声，而不是返回一个假的字符。
        => Assert.ThrowsExactly<FormatException>(() => new VeloxJsonReader("   ").ReadText());

    [TestMethod]
    public void AMemberNameThatNeverCloses_IsRefused()
    {
        // 窗口路径：名字扫到文档末尾（TryPeek 交出 false），报的是未闭合。
        var reader = Window("{\"ab");

        Assert.IsTrue(reader.BeginObject(out _, out _));
        Assert.ThrowsExactly<FormatException>(() => reader.NextMember());
    }
}
