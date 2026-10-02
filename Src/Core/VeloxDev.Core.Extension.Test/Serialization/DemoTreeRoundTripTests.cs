using System.Linq;
using Demo.ViewModels;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// The demo's own tree type survives a round trip.
/// </summary>
/// <remarks>
/// The frozen documents cover the framework's <c>TreeDefaultViewModel</c>; the demo's <c>TreeViewModel</c> is a
/// different type with its own members, and the seven demos save and load it through
/// <c>Serialize()</c> / <c>TryDeserialize&lt;TreeViewModel&gt;</c>. A failure here is silent in the app — the load
/// path is <c>if (success &amp;&amp; result is not null)</c>, so a refusal looks like the button doing nothing.
/// <para>
/// "Survives" is asserted on values, not on the deserializer not refusing: a tree that reads back with its own
/// members defaulted would still pass an <c>IsNotNull</c>, and the panels would simply show an empty canvas.
/// </para>
/// </remarks>
[TestClass]
public class DemoTreeRoundTripTests
{
    private static string Head(string json) => json.Length <= 600 ? json : json.Substring(0, 600) + "…";

    [TestMethod]
    public void AnEmptyDemoTreeRoundTrips()
    {
        var tree = new TreeViewModel();
        tree.AgentLog.Add("[User] do the thing");
        tree.ConversationMarkdown = "## conversation";
        tree.UseStreamingAgentResponse = false;

        var json = tree.Serialize();

        Assert.IsTrue(
            json.TryDeserialize<TreeViewModel>(out var restored),
            $"the demo's own tree must read back.\n--- json head ---\n{Head(json)}");
        Assert.IsNotNull(restored);

        // These four are declared by the demo, not by the framework tree the golden files cover — a writer
        // that only walked the base type would drop every one of them without refusing.
        Assert.HasCount(1, restored!.AgentLog, "the demo's own log collection must survive");
        Assert.AreEqual("[User] do the thing", restored.AgentLog[0], "the log lines must survive, not just the collection");
        Assert.AreEqual("## conversation", restored.ConversationMarkdown, "the demo's own scalar must survive");
        Assert.IsFalse(restored.UseStreamingAgentResponse, "the demo's own flag must survive");
    }

    [TestMethod]
    public void ADemoTreeWithANodeRoundTrips()
    {
        var tree = new TreeViewModel();
        var node = new TimerNodeViewModel { Title = "source", IntervalMilliseconds = 250 };
        tree.GetHelper().CreateNode(node);

        var json = tree.Serialize();

        Assert.IsTrue(
            json.TryDeserialize<TreeViewModel>(out var restored),
            $"a tree with a node must read back.\n--- json head ---\n{Head(json)}");

        var restoredNode = restored!.Nodes.OfType<TimerNodeViewModel>().SingleOrDefault();
        Assert.IsNotNull(restoredNode, "the node must come back as the demo's own type, not a base template");
        Assert.AreEqual("source", restoredNode!.Title, "the node's own property must survive");
        Assert.AreEqual(250, restoredNode.IntervalMilliseconds, "the node's own property must survive");
    }

    [TestMethod]
    public void ADemoTreeWithAConnectionRoundTrips()
    {
        var tree = new TreeViewModel();
        var source = new TimerNodeViewModel();
        var sink = new TimerNodeViewModel();
        tree.GetHelper().CreateNode(source);
        tree.GetHelper().CreateNode(sink);

        // A slot's default channel is None, so a connection is only ever made after the demos set the
        // channels — the same two lines WorkflowDemoSession.Connect relies on.
        source.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        tree.GetHelper().SendConnection(source.OutputSlot);
        tree.GetHelper().ReceiveConnection(sink.InputSlot);
        Assert.HasCount(1, tree.Links, "precondition: the two nodes are connected before saving");

        var json = tree.Serialize();

        Assert.IsTrue(
            json.TryDeserialize<TreeViewModel>(out var restored),
            $"a tree with a connection must read back.\n--- json head ---\n{Head(json)}");
        Assert.HasCount(1, restored!.Links,
            "the connection must survive — a dropped link leaves a tree that looks fine and routes nothing");

        // The endpoints must hang off the restored tree's own nodes. A link whose slots were rebuilt as
        // standalone copies would pass the count and still be dead.
        var link = restored.Links[0];
        var restoredNodes = restored.Nodes.ToList();
        Assert.IsTrue(restoredNodes.Contains(link.Sender.Parent!), "the sender slot must belong to a restored node");
        Assert.IsTrue(restoredNodes.Contains(link.Receiver.Parent!), "the receiver slot must belong to a restored node");
    }
}
