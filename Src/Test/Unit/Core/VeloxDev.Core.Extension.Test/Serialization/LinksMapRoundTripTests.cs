using System.IO;
using System.Threading.Tasks;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The tree's <c>LinksMap</c> — an interface-keyed map of interface-keyed maps — across a round trip.
/// </summary>
/// <remarks>
/// This is the most expensive shape the format carries: the key is an interface, so it cannot be written as a
/// name, and the document has to carry a reference id instead. The benchmark corpus leaves the map empty, which
/// is why the reader's map-key path had no test — a connected tree is the only thing that reaches it.
/// </remarks>
[TestClass]
public class LinksMapRoundTripTests
{
    /// <summary>A tree with one connection, so its <c>LinksMap</c> holds one entry.</summary>
    private static TreeDefaultViewModel BuildConnectedTree()
    {
        var tree = new TreeDefaultViewModel();

        var source = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(source);
        source.Anchor = new Anchor(100d, 100d, 0);
        source.Size = new Size { Width = 200d, Height = 100d };
        // OneTarget = 「可主动连出的目标数」，也就是发送端；OneSource 相反，是被连入的那一端。
        var output = new SlotDefaultViewModel { Channel = SlotChannel.OneTarget };
        source.CreateSlotCommand.Execute(output);

        var target = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(target);
        target.Anchor = new Anchor(400d, 100d, 0);
        target.Size = new Size { Width = 200d, Height = 100d };
        var input = new SlotDefaultViewModel { Channel = SlotChannel.OneSource };
        target.CreateSlotCommand.Execute(input);

        tree.SendConnectionCommand.Execute(output);
        tree.ReceiveConnectionCommand.Execute(input);

        return tree;
    }

    [TestMethod]
    public void AConnectedTree_CarriesItsLinksMapAcrossTheRoundTrip()
    {
        var tree = BuildConnectedTree();

        Assert.AreEqual(1, tree.Links.Count, "the connection was made");
        Assert.AreEqual(1, tree.LinksMap.Count, "and the map indexes it — otherwise this test proves nothing");

        var restored = VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(VeloxJsonSerializer.Serialize(tree))!;

        Assert.AreEqual(1, restored.Links.Count, "the link came back");
        Assert.AreEqual(1, restored.LinksMap.Count, "the map came back with it");
    }

    [TestMethod]
    public async Task AConnectedTree_SurvivesTheAsyncRoundTrip()
    {
        // 接口键的写与读在异步面各有一份实现（键写成引用 id 的那条路），这条把两侧都走一遍。
        var tree = BuildConnectedTree();
        var document = VeloxJsonSerializer.Serialize(tree);

        using var text = new StringWriter();
        await VeloxJsonSerializer.WriteToAsync(text, tree);
        Assert.AreEqual(document, text.ToString(), "the async writer disagrees with the sync one");

        var restored = await VeloxJsonSerializer.DeserializeAsync<TreeDefaultViewModel>(new ChunkedTextReader(document, 1));

        Assert.AreEqual(1, restored!.LinksMap.Count, "the interface-keyed map came back on the async face too");
    }

    [TestMethod]
    public void AConnectedTree_ReproducesItsOwnDocument()
    {
        // 幂等：删掉语料里没有的那个形状之后，写出来的必须还是同一份。
        var tree = BuildConnectedTree();
        var document = VeloxJsonSerializer.Serialize(tree);

        var restored = VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(document)!;

        Assert.AreEqual(document, VeloxJsonSerializer.Serialize(restored));
    }
}
