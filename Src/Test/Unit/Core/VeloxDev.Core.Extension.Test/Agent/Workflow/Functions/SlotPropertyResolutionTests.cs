using System.Linq;
using Demo.ViewModels;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// A slot that exists is reachable by the property that holds it.
/// </summary>
/// <remarks>
/// <para>
/// Three tools resolve a slot from a property name — <c>ListSlotProperties</c> to list them, <c>ResolveSlotId</c>
/// to turn a name into a runtime id, and the connect family to aim a link at one. All three keyed off
/// <c>AIContextFlags.IsSingleSlot</c>, and the context-tree generator could not compute that flag for a type
/// declared <c>[WorkflowBuilder.Slot&lt;T&gt;]</c>: the <c>IWorkflowSlotViewModel</c> interface is injected by
/// <i>another</i> generator, in the same pass, and generators cannot see each other's output.
/// </para>
/// <para>
/// The live consequence was a report from a real session: <c>EnumSelectorNodeViewModel.InputSlot</c> was missing
/// from <c>ListSlotProperties</c>, absent from the property-to-slot map (<c>GetNodeDetail</c> showed the slot with
/// no <c>prop</c>), and every name-based connect attempt on it failed — while the slot was sitting right there.
/// The same missing flag also left <c>ComponentPatcher</c> willing to assign over a slot property, which is the
/// one path that bypasses <c>CreateSlotCommand</c>.
/// </para>
/// <para>
/// The fix is in the generator (`IsSingleSlotType` gains the <c>[WorkflowBuilder.Slot]</c> arm the rest of the
/// codebase already uses), and in the tools as a runtime fallback for a consumer built against an older one.
/// </para>
/// </remarks>
[TestClass]
public class SlotPropertyResolutionTests
{
    private static WorkflowAgentScope ScopeWith(params object[] nodes)
    {
        var tree = new TreeDefaultViewModel();
        foreach (var node in nodes) tree.GetHelper().CreateNode((IWorkflowNodeViewModel)node);
        return new WorkflowAgentScope(tree);
    }

    private static JObject Json(string result) => JObject.Parse(result);

    private static JArray JsonArray(string result) => JArray.Parse(result);

    /// <summary>The report's §4: the lazily-created single slot is listed.</summary>
    [TestMethod]
    public void ListSlotProperties_ReportsTheSingleSlot()
    {
        var scope = ScopeWith(new EnumSelectorNodeViewModel());

        var listed = JsonArray(WorkflowToolInvoker.Invoke(scope, "ListSlotProperties", ("nodeIndex", 0)));

        var names = listed.Select(e => e!["name"]!.Value<string>()).ToArray();
        CollectionAssert.Contains(names, "InputSlot",
            "the node's input slot must appear, or a caller concludes the node has none");
        CollectionAssert.Contains(names, "OutputSlots", "and the enumerator still appears");
    }

    /// <summary>The report's §3 B, for the single slot the name path used to give up on.</summary>
    [TestMethod]
    public void ResolveSlotId_FindsASingleSlot()
    {
        var node = new EnumSelectorNodeViewModel();
        var scope = ScopeWith(node);

        var resolved = Json(WorkflowToolInvoker.Invoke(scope, "ResolveSlotId",
            ("nodeIndex", 0), ("propertyName", "InputSlot")));

        Assert.AreEqual("ok", resolved["status"]?.Value<string>(), resolved.ToString());
        Assert.AreEqual(((IWorkflowIdentifiable)node.InputSlot).RuntimeId, resolved["id"]?.Value<string>(),
            "and the id it hands back is the slot's own");
    }

    /// <summary>
    /// The report's §3 B, for a slot inside an enumerator.
    /// </summary>
    /// <remarks>
    /// An enumerator carries the <c>IsSlotCollection</c> flag as well as its own, and <c>ResolveSlotId</c> checked
    /// the collection branch first — where it demanded an <c>IList</c>. A <c>SlotEnumerator</c> holds its entries
    /// as <c>ConditionalSlot</c> and is not one, so the tool answered "out of range or null" about a collection
    /// that was neither.
    /// </remarks>
    [TestMethod]
    public void ResolveSlotId_FindsASlotInsideAnEnumerator()
    {
        var node = new PythonScriptNodeViewModel();
        var scope = ScopeWith(node);

        WorkflowToolInvoker.Invoke(scope, "SetEnumSlotCollection",
            ("nodeIndex", 0),
            ("propertyName", "InputSlots"),
            ("selectorTypeOrJson", """{"Ports":[{"Name":"stats"},{"Name":"dist"}]}"""),
            ("nonEnumTypeName", typeof(PythonPortProvider).FullName!));

        var resolved = Json(WorkflowToolInvoker.Invoke(scope, "ResolveSlotId",
            ("nodeIndex", 0), ("propertyName", "InputSlots"), ("collectionIndex", 1)));

        Assert.AreEqual("ok", resolved["status"]?.Value<string>(), resolved.ToString());
        Assert.AreEqual(((IWorkflowIdentifiable)node.InputSlots.Items[1].Slot).RuntimeId,
            resolved["id"]?.Value<string>());
        Assert.AreEqual("dist", resolved["label"]?.Value<string>(), "and it says which port it picked");
    }

    /// <summary>The property-to-slot map knows the single slot, which is what <c>GetNodeDetail</c> reports.</summary>
    [TestMethod]
    public void GetNodeDetail_MapsTheSingleSlotToItsProperty()
    {
        var scope = ScopeWith(new EnumSelectorNodeViewModel());

        var detail = Json(WorkflowToolInvoker.Invoke(scope, "GetNodeDetail", ("nodeIndex", 0)));
        var slots = (JArray)detail["slots"]!;

        var mapped = slots.SingleOrDefault(s => s!["prop"]?.Value<string>() == "InputSlot");
        Assert.IsNotNull(mapped,
            "the input slot must carry the property that holds it — without it no name-based tool can aim at it: " + detail);
    }

    /// <summary>The report's §3 C, end to end: a link can be aimed at the single slot by name.</summary>
    [TestMethod]
    public void ConnectByProperty_ResolvesTheSingleSlot()
    {
        var producer = new NodeDefaultViewModel();
        var consumer = new EnumSelectorNodeViewModel();
        var senderSlot = new SlotDefaultViewModel { Channel = SlotChannel.OneTarget };

        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(producer);
        tree.GetHelper().CreateNode(consumer);
        producer.GetHelper().CreateSlot(senderSlot);

        var scope = new WorkflowAgentScope(tree);
        var result = Json(WorkflowToolInvoker.Invoke(scope, "ConnectByProperty",
            ("senderNodeIndex", 0), ("senderProperty", "Slots"),
            ("receiverNodeIndex", 1), ("receiverProperty", "InputSlot")));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.HasCount(1, tree.Links);
        Assert.AreSame(consumer.InputSlot, tree.Links[0].Receiver);
    }

    /// <summary>
    /// An enumerator is told apart from a plain slot collection, because the two need different tools.
    /// </summary>
    /// <remarks>
    /// An enumerator carries both flags, so it passed the "is this a slot collection" guard and then failed on
    /// "the collection is null" — about a collection that is not null, is not empty, and is simply not an
    /// <c>IList</c>. The answer should name the tool that does own it.
    /// </remarks>
    [TestMethod]
    public void AddSlotToCollection_OnAnEnumerator_PointsAtTheSelectorTool()
    {
        var scope = ScopeWith(new PythonScriptNodeViewModel());

        var refused = Json(WorkflowToolInvoker.Invoke(scope, "AddSlotToCollection",
            ("nodeIndex", 0), ("propertyName", "InputSlots"),
            ("fullSlotTypeName", typeof(SlotDefaultViewModel).FullName!), ("channel", "OneBoth")));

        Assert.AreEqual("error", refused["status"]?.Value<string>(), refused.ToString());
        StringAssert.Contains(refused["message"]!.Value<string>()!, "SetEnumSlotCollection",
            "the refusal has to name the tool that can do it");
    }

    /// <summary>
    /// A slot property cannot be assigned over — the one path that would bypass <c>CreateSlotCommand</c>.
    /// </summary>
    [TestMethod]
    public void PatchNodeProperties_RefusesToAssignASlotProperty()
    {
        var scope = ScopeWith(new EnumSelectorNodeViewModel());

        var patch = Json(WorkflowToolInvoker.Invoke(scope, "PatchNodeProperties",
            ("nodeIndex", 0), ("jsonPatch", """{"InputSlot":null}""")));

        var detail = ((JArray)patch["details"]!).Single(d => d!["property"]!.Value<string>() == "InputSlot");
        Assert.AreEqual("rejected", detail!["status"]?.Value<string>(),
            "a slot is created by its command, never assigned: " + patch);
    }
}
