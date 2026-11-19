using System.IO;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The three spellings the writer's fast paths are allowed to touch: escaping, indentation, and numbers.
/// </summary>
/// <remarks>
/// <para>
/// The golden documents already pin a whole tree, but they pin it through the object graph — a spelling used by
/// no type in that graph is not covered by them at all. These cases drive <see cref="VeloxJsonWriter"/> directly
/// so every escape form, the indent depth and the number shapes are each frozen on their own.
/// </para>
/// <para>
/// The point is not the values themselves but that the writer's fast paths — the whole-string write when nothing
/// needs escaping, the cached indent, the pre-scanned runs — produce exactly what the slow path did. A fast path
/// that returns the same <i>text</i> by a different route is the only kind this format accepts.
/// </para>
/// </remarks>
[TestClass]
public class SerializationTextSpellingTests
{
    private static string Write(Action<VeloxJsonWriter> write, bool indented = false)
    {
        using var text = new StringWriter();
        write(new VeloxJsonWriter(text, indented));
        return text.ToString();
    }

    [TestMethod]
    public void EveryEscapableCharacter_IsSpelledTheWayTheFormatSpellsIt()
    {
        // 输入：引号、反斜杠、五个具名控制字符、一个非具名的 C0、以及非 ASCII。
        // 转义的是前者，非 ASCII 原样留下 —— 这一条是格式的一部分，不是实现细节。
        var written = Write(w => w.WriteString("q\" b\\ \b\f\n\r\t \u0001 中文"));

        Assert.AreEqual("\"q\\\" b\\\\ \\b\\f\\n\\r\\t \\u0001 中文\"", written);
    }

    [TestMethod]
    public void AStringWithNothingToEscape_IsWrittenUnchanged()
    {
        // 走的是「整串一次写出」那条快路径；它必须与逐字符那条路产出同一串。
        var written = Write(w => w.WriteString("plain-id-0123456789"));

        Assert.AreEqual("\"plain-id-0123456789\"", written);
    }

    [TestMethod]
    public void Indentation_IsTwoSpacesPerLevelOverThePlatformsNewLine()
    {
        var written = Write(w =>
        {
            w.WriteStartObjectWithoutReference();
            w.WriteMemberName("a");
            w.WriteStartArray();
            w.WriteNextElement();
            w.WriteInt32(1);
            w.WriteEndArray();
            w.WriteEndObject();
        }, indented: true);

        var nl = Environment.NewLine;
        Assert.AreEqual("{" + nl + "  \"a\": [" + nl + "    1" + nl + "  ]" + nl + "}", written);
    }

    [TestMethod]
    public void Numbers_KeepTheirShapes()
    {
        // 整数值必须带 .0，非有限值写成字符串，这不只是拼写 —— 反序列化侧按它分辨 double 与 int。
        var written = Write(w =>
        {
            w.WriteStartArray();
            foreach (var value in new[] { 2d, 2.5d, -0.0d, 1e+30 })
            {
                w.WriteNextElement();
                w.WriteDouble(value);
            }

            w.WriteNextElement();
            w.WriteInt32(2);
            w.WriteEndArray();
        });

        Assert.AreEqual("[2.0,2.5,-0.0,1E+30,2]", written);
    }

    [TestMethod]
    public void EmptyContainers_StayOnOneLine()
    {
        var written = Write(w =>
        {
            w.WriteStartArray();
            w.WriteEndArray();
            w.WriteStartObjectWithoutReference();
            w.WriteEndObject();
        }, indented: true);

        Assert.AreEqual("[]{}", written);
    }
}
