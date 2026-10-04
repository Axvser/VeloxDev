using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// JSON string escapes, through every read entry and on both faces.
/// </summary>
/// <remarks>
/// <para>
/// No document in the corpus carries one: the frozen goldens are all plain text, so the decoder's escape tail
/// (<c>ReadEscapedTail</c> and its async twin) was never reached — and neither was <c>DecodeMemberName</c>, the
/// fast path that only runs when a member name itself carries an escape.
/// </para>
/// <para>
/// The writer produces the escapes, so holding the four read entries to a round trip is enough to hold the
/// decoder to the writer: whatever the document spells, the same string must come back.
/// </para>
/// </remarks>
[TestClass]
public partial class StringEscapeTests
{
    /// <summary>One string with every escape in it, and a map whose keys carry escapes too.</summary>
    internal sealed partial class TextModel
    {
        [VeloxProperty] private string? text;
        [VeloxProperty] private Dictionary<string, int>? named;
    }

    // 每个转义都来一遍：引号、反斜杠、斜杠、\b \f \n \r \t；\u 来两种 —— BMP 内的，
    // 以及要拆成代理对的高位字符；末尾再放一个控制字符。
    private const string Awkward =
        "q:\" b:\\ s:/ bs:\b ff:\f nl:\n cr:\r tab:\t bmp:\u4e2d pair:\U0001F600 ctl:\u0001";

    private static TextModel Sample() => new()
    {
        Text = Awkward,
        Named = new Dictionary<string, int>
        {
            ["plain"] = 1,
            ["quo\"te"] = 2,
            ["back\\slash"] = 3,
            ["new\nline"] = 4,
        },
    };

    private static void AssertSame(TextModel restored, string entry)
    {
        Assert.AreEqual(Awkward, restored.Text, entry + ": the string came back changed");
        Assert.IsNotNull(restored.Named, entry + ": the map came back null");
        Assert.AreEqual(4, restored.Named!.Count, entry + ": the map lost a key");

        // 键里带引号/反斜杠/换行的那些：读侧要能把成员名解回来，不能按原文去比。
        Assert.AreEqual(2, restored.Named["quo\"te"], entry + ": an escaped member name was not decoded");
        Assert.AreEqual(3, restored.Named["back\\slash"], entry + ": a doubled backslash in a name");
        Assert.AreEqual(4, restored.Named["new\nline"], entry + ": a control escape in a name");
    }

    [TestMethod]
    public void TheDocumentReallyCarriesEscapes()
    {
        // 前提：写入器把这些字符转义出去（否则下面的读测试什么也没覆盖到）。
        var json = VeloxJsonSerializer.Serialize(Sample());

        StringAssert.Contains(json, "\\\"", "a quote must be escaped");
        StringAssert.Contains(json, "\\\\", "a backslash must be escaped");
        StringAssert.Contains(json, "\\n", "a newline must be escaped");
    }

    [TestMethod]
    public void AWholeString_DecodesEveryEscape()
        => AssertSame(VeloxJsonSerializer.Deserialize<TextModel>(VeloxJsonSerializer.Serialize(Sample()))!, "string");

    [TestMethod]
    public void AChunkedSource_DecodesEveryEscape()
    {
        // 每个转义序列都被切成单字符：`\`、`u`、四位数各自跨一次续读。
        var json = VeloxJsonSerializer.Serialize(Sample());
        var restored = (TextModel)VeloxJsonSerializer.Deserialize(new ChunkedTextReader(json, 1), typeof(TextModel))!;

        AssertSame(restored, "chunked TextReader");
    }

    [TestMethod]
    public async Task AChunkedSource_DecodesEveryEscapeOnTheAsyncFace()
    {
        var json = VeloxJsonSerializer.Serialize(Sample());
        var restored = await VeloxJsonSerializer.DeserializeAsync<TextModel>(new ChunkedTextReader(json, 1));

        AssertSame(restored!, "chunked TextReader, async");
    }

    [TestMethod]
    public async Task TheAsyncWriter_EscapesTheSameWayTheSyncOneDoes()
    {
        // 异步写入器把字符先攒进缓冲（转义那条路因此是另一份实现），落盘的时机不同、字节必须相同。
        var sync = VeloxJsonSerializer.Serialize(Sample());

        using var text = new StringWriter();
        await VeloxJsonSerializer.WriteToAsync(text, Sample());

        Assert.AreEqual(sync, text.ToString(), "the two writers disagree on the escapes");
    }

    [TestMethod]
    public async Task AStream_DecodesEveryEscapeOnTheAsyncFace()
    {
        var json = VeloxJsonSerializer.Serialize(Sample());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var restored = await VeloxJsonSerializer.DeserializeAsync<TextModel>(stream);

        AssertSame(restored!, "stream, async");
    }
}
