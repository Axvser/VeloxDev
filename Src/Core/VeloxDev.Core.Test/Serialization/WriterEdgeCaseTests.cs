using System;
using System.IO;
using System.Threading.Tasks;
using VeloxDev.Serialization;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The writer's corners: the null guards on its surface, the indentation past its cache, and the buffer's
/// growth and handover — all of which a small document never reaches.
/// </summary>
[TestClass]
public class WriterEdgeCaseTests
{
    [TestMethod]
    public void TheGuards_RefuseWhatTheyMust()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new VeloxJsonWriter(null!));

        var writer = new VeloxJsonWriter(new StringWriter());
        Assert.ThrowsExactly<ArgumentNullException>(() => writer.WriteStartObject(null!, null));
        Assert.ThrowsExactly<ArgumentNullException>(() => writer.GetOrAddReference(null!));
    }

    [TestMethod]
    public async Task TheAsyncMemberName_RefusesNoName()
    {
        var writer = new VeloxJsonWriter(new StringWriter(), indented: true, async: true);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => writer.WriteMemberNameAsync(null!));
    }

    [TestMethod]
    public void ADocumentDeeperThanTheIndentCache_FallsBackToTheLoop()
    {
        // 缩进预生成到 64 层。再深就退回「每层写一次两个空格」的循环 —— 极深文档才会走到。
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: true);

        for (var i = 0; i < 70; i++) writer.WriteStartObjectWithoutReference();
        writer.WriteMemberName("a");
        writer.WriteInt32(1);
        for (var i = 0; i < 70; i++) writer.WriteEndObject();

        Assert.IsTrue(
            text.ToString().Contains(new string(' ', 70 * 2), StringComparison.Ordinal),
            "the deepest member is still indented by its depth");
    }

    [TestMethod]
    public async Task ADocumentDeeperThanTheIndentCache_FallsBackOnTheAsyncFace()
    {
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: true, async: true);

        for (var i = 0; i < 70; i++) writer.WriteStartObjectWithoutReference();
        writer.WriteMemberName("a");
        writer.WriteInt32(1);
        for (var i = 0; i < 70; i++) writer.WriteEndObject();

        await writer.CompleteAsync();

        Assert.IsTrue(
            text.ToString().Contains(new string(' ', 70 * 2), StringComparison.Ordinal),
            "the asynchronous buffer indents the same way");
    }

    [TestMethod]
    public async Task ABigDocument_GrowsTheBufferAndHandsItOverBetweenTokens()
    {
        // 一个 token 比整个缓冲区还长：Append 要先增长；攒过阈值之后 MakeRoom 先 await 刷一次。
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);

        for (var i = 0; i < 3; i++) await writer.WriteStringAsync(new string((char)('a' + i), 3000));
        await writer.WriteStringAsync(new string('z', 20000));
        await writer.CompleteAsync();

        var document = text.ToString();
        StringAssert.Contains(document, new string('c', 3000), "every token reached the output");
        StringAssert.Contains(document, new string('z', 20000), "including the one longer than the buffer");
    }

    [TestMethod]
    public async Task ACharAppendedToAFullBuffer_GrowsIt()
    {
        // 刚好填满缓冲区之后再来一个字符：Append(char) 那条增长分支。
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);

        writer.WriteRawValue(new string('x', 8192));
        writer.WriteMemberName("a");
        writer.WriteInt32(7);
        await writer.CompleteAsync();

        Assert.IsTrue(
            text.ToString().StartsWith(new string('x', 8192), StringComparison.Ordinal),
            "the oversized raw value is unchanged");
        StringAssert.Contains(text.ToString(), "\"a\":7", "and the member after it is written too");
    }

    [TestMethod]
    public async Task TheBuffer_IsHandedOverOnlyWhenItHoldsSomething()
    {
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);

        // 空的：什么都不交出去，也不碰输出。
        writer.FlushBuffer();
        await writer.CompleteAsync();
        Assert.AreEqual(string.Empty, text.ToString(), "an untouched writer writes nothing");

        // 有数据：同步的 FlushBuffer 把缓冲整段写进 TextWriter 并清空。
        writer.WriteString("x");
        writer.FlushBuffer();
        Assert.AreEqual("\"x\"", text.ToString(), "the buffered characters arrive in one handover");
    }

    [TestMethod]
    public void AnObjectWhoseTypeIsRegistered_WritesItsName()
    {
        // 声明类型与运行期类型不同，而运行期类型在注册表里有名字 —— `$type` 写的就是那个名字。
        VeloxJsonRegistry.RegisterName(typeof(WriterEdgeCaseTests), "VeloxDev.Core.Test.WriterEdgeCaseTests, VeloxDev.Core.Test");

        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false);

        Assert.IsTrue(writer.WriteStartObject(this, typeof(object)));
        writer.WriteEndObject();

        StringAssert.Contains(
            text.ToString(), "\"$type\":\"VeloxDev.Core.Test.WriterEdgeCaseTests, VeloxDev.Core.Test\"",
            "the registered name, not the reflection name");
    }
}
