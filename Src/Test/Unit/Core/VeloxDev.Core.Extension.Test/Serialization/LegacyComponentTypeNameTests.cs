using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Documents written before the component rename still read: their <c>$type</c> carries the old
/// <c>*DefaultViewModel</c> names, and the registry resolves them to the current components.
/// </summary>
[TestClass]
public class LegacyComponentTypeNameTests
{
    [TestMethod]
    public void APreRenameDocumentStillReads()
    {
        var tree = new TreeDefaultComponent();
        var source = new NodeDefaultComponent();
        var sink = new NodeDefaultComponent();
        var output = new SlotDefaultComponent { Channel = SlotChannel.OneTarget };
        var input = new SlotDefaultComponent { Channel = SlotChannel.OneSource };

        tree.GetHelper().CreateNode(source);
        tree.GetHelper().CreateNode(sink);
        source.GetHelper().CreateSlot(output);
        sink.GetHelper().CreateSlot(input);
        tree.GetHelper().SendConnection(output);
        tree.GetHelper().ReceiveConnection(input);

        var json = tree.Serialize();
        Assert.IsFalse(json.Contains("DefaultViewModel"), "the writer must only emit the current names");

        // The shape a document written before the rename has.
        json = json
            .Replace("NodeDefaultComponent", "NodeDefaultViewModel")
            .Replace("SlotDefaultComponent", "SlotDefaultViewModel")
            .Replace("LinkDefaultComponent", "LinkDefaultViewModel")
            .Replace("TreeDefaultComponent", "TreeDefaultViewModel");

        Assert.IsTrue(
            json.TryDeserialize<TreeDefaultComponent>(out var restored),
            $"a document with the pre-rename $type names must read back.\n--- json head ---\n{Head(json)}");

        Assert.IsNotNull(restored);
        Assert.HasCount(2, restored!.Nodes);
        Assert.IsInstanceOfType<NodeDefaultComponent>(restored.Nodes[0]);
        Assert.HasCount(1, restored.Links);
        Assert.IsInstanceOfType<LinkDefaultComponent>(restored.Links[0]);
    }

    private static string Head(string json) => json.Length <= 600 ? json : json.Substring(0, 600) + "…";
}
