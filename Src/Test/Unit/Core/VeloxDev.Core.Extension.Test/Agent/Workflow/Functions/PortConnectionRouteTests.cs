using System.Linq;
using Demo.ViewModels;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The two promises the tool descriptions now make about selector-driven ports.
/// </summary>
/// <remarks>
/// <para>
/// Neither is a new capability — both are routes that already worked and that nothing told the model about. The
/// report this file comes from has them as failures: <c>ConnectEnumSlot</c> "does not support collection or
/// ISlotProvider ports" (§3 A) and "a new Python node's ports are lazily created, so it cannot be connected to
/// (§2). Both statements are about discoverability, not about what the code can do, so the fix is in the
/// descriptions — and a description is a claim, which means it needs a test or it is only an assertion.
/// </para>
/// <para>
/// The first one is the sharper of the two: <c>receiverCondition</c> is matched against whatever each slot
/// answers to, and for an <c>ISlotProvider</c>-driven enumerator that is the <b>port name</b> — a string, not an
/// enum member. Nothing in the parameter's old text ("enum name or True/False") said so.
/// </para>
/// </remarks>
[TestClass]
public class PortConnectionRouteTests
{
    /// <summary>
    /// A provider-driven receiver is reachable by naming the port: <c>receiverSlot</c> = the enumerator,
    /// <c>receiverCondition</c> = the port.
    /// </summary>
    [TestMethod]
    public void ConnectEnumSlot_ReachesAProviderDrivenReceiver_ByPortName()
    {
        var source = new EnumSelectorNodeViewModel();     // OutputSlots, selected by VoltageRange
        var target = new PythonScriptNodeViewModel();     // InputSlots, selected by PythonPortProvider

        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(source);
        tree.GetHelper().CreateNode(target);

        var scope = new WorkflowAgentScope(tree);
        WorkflowToolInvoker.Invoke(scope, "SetEnumSlotCollection",
            ("nodeIndex", 1), ("propertyName", "InputSlots"),
            ("selectorTypeOrJson", """{"Ports":[{"Name":"stats"},{"Name":"dist"}]}"""),
            ("nonEnumTypeName", typeof(PythonPortProvider).FullName!));

        var result = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ConnectEnumSlot",
            ("senderNodeIndex", 0), ("senderProperty", "OutputSlots"), ("senderCondition", "Low"),
            ("receiverNodeIndex", 1), ("receiverSlot", "InputSlots"), ("receiverCondition", "stats")));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.HasCount(1, tree.Links);
        Assert.AreSame(target.InputSlots.Items[0].Slot, tree.Links[0].Receiver,
            "the link must land on the port named 'stats', which is what receiverCondition picks");
    }

    /// <summary>
    /// And the mistake the description warns about is the one the tool refuses — with that explanation.
    /// </summary>
    [TestMethod]
    public void ConnectEnumSlot_RefusesAReceiverEnumerator_WithoutACondition()
    {
        var source = new EnumSelectorNodeViewModel();
        var target = new PythonScriptNodeViewModel();

        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(source);
        tree.GetHelper().CreateNode(target);

        var scope = new WorkflowAgentScope(tree);
        WorkflowToolInvoker.Invoke(scope, "SetEnumSlotCollection",
            ("nodeIndex", 1), ("propertyName", "InputSlots"),
            ("selectorTypeOrJson", """{"Ports":[{"Name":"stats"}]}"""),
            ("nonEnumTypeName", typeof(PythonPortProvider).FullName!));

        var refused = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ConnectEnumSlot",
            ("senderNodeIndex", 0), ("senderProperty", "OutputSlots"), ("senderCondition", "Low"),
            ("receiverNodeIndex", 1), ("receiverSlot", "InputSlots")));

        Assert.AreEqual("error", refused["status"]?.Value<string>(), refused.ToString());
        StringAssert.Contains(refused["message"]!.Value<string>()!, "receiverCondition",
            "the refusal has to name the argument that would have made it work — that is the whole difference "
            + "between this and the report's dead end");
        Assert.IsEmpty(tree.Links, "and nothing may be connected by a call that failed");
    }

    /// <summary>
    /// A freshly created selector-driven node has no ports — which is what <c>CreateNode</c> now says.
    /// </summary>
    [TestMethod]
    public void CreateNode_LeavesASelectorDrivenNodeWithNoPorts()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        var created = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(PythonScriptNodeViewModel).FullName!)));
        Assert.AreEqual("ok", created["status"]?.Value<string>(), created.ToString());

        var listed = JArray.Parse(WorkflowToolInvoker.Invoke(scope, "ListSlotProperties", ("nodeIndex", 0)));
        var inputs = listed.Single(e => e!["name"]!.Value<string>() == "InputSlots")!;
        Assert.AreEqual(0, inputs["count"]!.Value<int>(),
            "the description promises count:0 means 'not configured yet' — so it must be 0, and it must be reported");
    }
}
