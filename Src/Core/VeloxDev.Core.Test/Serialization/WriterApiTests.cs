using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.Serialization;

/// <summary>
/// The writer's own surface, and the two annotation types: members the serializer happens not to reach.
/// </summary>
/// <remarks>
/// The reference registry's "give it a new id" arm is dead weight for the documents the serializer writes — an
/// interface-keyed map's keys have always been written before the map — and the indentation cache falls back to
/// a loop past its 64th level, which no realistic document reaches. The attribute types are never instantiated
/// by the runtime at all: they are read through reflection. Each is public API, so each gets a test here.
/// </remarks>
[TestClass]
public class WriterApiTests
{
    [TestMethod]
    public void AReferenceIsHandedOutOnce_AndTheCountFollows()
    {
        var writer = new VeloxJsonWriter(new StringWriter());

        var first = new object();
        var second = new object();

        Assert.AreEqual(1, writer.GetOrAddReference(first), "the first sighting takes the next id");
        Assert.AreEqual(1, writer.GetOrAddReference(first), "and the same object keeps it");
        Assert.AreEqual(2, writer.GetOrAddReference(second), "the next one takes the id after");
        Assert.AreEqual(2, writer.ReferenceCount, "the count is how many ids were handed out");

        Assert.ThrowsExactly<ArgumentNullException>(() => writer.GetOrAddReference(null!));
    }

    [TestMethod]
    public void ARawToken_IsWrittenExactlyAsItStands()
    {
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false);

        // 同步写入器不缓冲：写完就在 TextWriter 里，没有 flush 这一步。
        writer.WriteRawValue("12345678901234567890");

        Assert.AreEqual("12345678901234567890", text.ToString(), "a token with no dedicated method is copied through");
    }

    [TestMethod]
    public async Task ARawToken_IsWrittenExactlyAsItStandsOnTheAsyncFace()
    {
        var text = new StringWriter();
        var writer = new VeloxJsonWriter(text, indented: false, async: true);

        await writer.WriteRawValueAsync("12345678901234567890");
        await writer.CompleteAsync();

        Assert.AreEqual("12345678901234567890", text.ToString());
    }

    [TestMethod]
    public void ADocumentDeeperThanTheIndentCache_FallsBackToTheLoop()
    {
        // 缩进预生成到 64 层；再深就退回每层写一次 "  " 的循环。深到 70 层把那一条走一遍。
        VeloxJsonValue node = VeloxJsonValue.From(1);
        for (var i = 0; i < 70; i++)
        {
            var array = new VeloxJsonArray();
            array.Add(node);
            node = array;
        }

        var json = node.ToJson(VeloxJsonFormat.Indented);

        Assert.IsTrue(json.Contains(Environment.NewLine, StringComparison.Ordinal), "it is still laid out over lines");

        // 深处那几层只能由循环写出来（缓存只到 64 层），缩进因此是 70×2 个空格。
        Assert.IsTrue(json.Contains(new string(' ', 70 * 2), StringComparison.Ordinal), "and still indented by depth");

        // 深到超过缓存也必须读得回来 —— 这是它唯一要保证的事。
        Assert.IsTrue(node.DeepEquals(VeloxJsonValue.Parse(json)), "the deep tree survives a round trip");
    }

    /// <summary>A node that overrides nothing, so the base implementations are the ones answering.</summary>
    private sealed class BareNode : VeloxJsonValue
    {
        internal override void WriteTo(TextWriter writer, VeloxJsonFormat format, int depth) => writer.Write("bare");
    }

    [TestMethod]
    public void TheBaseMembers_AnswerForANodeThatOverridesNothing()
    {
        var one = new BareNode();
        var other = new BareNode();

        Assert.IsFalse(one.IsNull, "the base says it is not the JSON literal");
        Assert.AreSame(one, one.Materialize(), "the base materialises to itself");
        Assert.IsTrue(one.DeepEquals(one), "and compares by identity");
        Assert.IsFalse(one.DeepEquals(other), "two nodes are not one node");
        Assert.IsFalse(one.DeepEquals(null), "and neither is nothing");
    }

    [TestMethod]
    public void ADocumentNamingATypeWithNoReader_IsRefused()
    {
        // 文档点名了一个注册表里没有的 `$type`：消息要带上它，才说得出是哪份文档的问题。
        var named = "{\"$id\": \"1\", \"$type\": \"No.Such.Type\"}";
        var withName = Assert.ThrowsExactly<InvalidOperationException>(
            () => VeloxJsonSerializer.ReadValue(new VeloxJsonReader(named), typeof(BareNode), null));
        StringAssert.Contains(withName.Message, "No.Such.Type", "the message names what the document carried");

        // 另一条臂：没有 `$type`，而声明类型自己就没有读器 —— 消息以句点收尾，不点任何名字。
        var plain = "{\"$id\": \"1\"}";
        var without = Assert.ThrowsExactly<InvalidOperationException>(
            () => VeloxJsonSerializer.ReadValue(new VeloxJsonReader(plain), typeof(BareNode), null));
        StringAssert.EndsWith(without.Message, ".", "and says nothing when the document says nothing");
    }

    [TestMethod]
    public void TheAnnotations_KeepWhatTheyWereGiven()
    {
        var rename = new ArchiveAttribute(ArchiveOptions.ReName, "Other");
        Assert.AreEqual(ArchiveOptions.ReName, rename.Options, "the option");
        Assert.AreEqual("Other", rename.Argument, "and the argument it needs");

        Assert.IsNull(new ArchiveAttribute(ArchiveOptions.KeepField).Argument, "an option with nothing to pass keeps a null");

        var twoRoots = new ArchivableAttribute(typeof(string), typeof(int));
        CollectionAssert.AreEqual(
            new[] { typeof(string), typeof(int) }, twoRoots.AdditionalRoots.ToArray(), "the roots it names");

        Assert.AreEqual(0, new ArchivableAttribute().AdditionalRoots.Count, "and none when it names none");
        Assert.AreEqual(0, new ArchivableAttribute(null!).AdditionalRoots.Count, "including an explicit null");
    }
}
