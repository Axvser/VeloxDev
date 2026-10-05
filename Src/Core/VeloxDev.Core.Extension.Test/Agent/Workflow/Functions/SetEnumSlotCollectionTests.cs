using System.Collections.Generic;
using System.Linq;
using Demo.ViewModels;
using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// A port provider the Agent can name but the archive cannot carry.
/// <para>
/// It is deliberately <b>not</b> <c>[Archivable]</c>: the whole point is that the context tree knows it — the
/// <c>[AgentContext]</c> below is what gives it a tree entry — while the archive generator compiled no reader for
/// it. That is the exact shape of the defect this file was written for, and it is the shape a consuming project's
/// own provider has on the day it forgets the annotation.
/// </para>
/// </summary>
[AgentContext(AgentLanguages.English, "Test-only port provider that sits outside the archive closure.")]
public class NotArchivablePortProvider : ISlotProvider
{
    /// <summary>The port names this provider would produce.</summary>
    public List<string> Names { get; set; } = [];

    /// <inheritdoc/>
    public IEnumerable<SlotDefinition> GetSlots() => Names.Select(n => new SlotDefinition(n, n));
}

/// <summary>
/// A selector enum the tool can actually resolve — because it carries <see cref="AgentContextAttribute"/>.
/// </summary>
/// <remarks>
/// The attribute is not decoration here, it is the mechanism: it is what makes the generator emit a type entry
/// and an accessor for the enum, and an accessor is what <c>AgentTypeResolver</c> looks up. Compare
/// <see cref="TestRouteKind"/>, which an earlier test file names from <c>[SlotSelectors]</c> and nothing else.
/// </remarks>
[AgentContext(AgentLanguages.English, "Test-only selector enum that the context tree carries as a type.")]
public enum TestPortKind
{
    /// <summary>The first member.</summary>
    Primary,

    /// <summary>The second member.</summary>
    Secondary,
}

/// <summary>
/// The four <c>SlotEnumerator</c> shapes <c>SetEnumSlotCollection</c> can be pointed at.
/// </summary>
[WorkflowBuilder.Node<NodeHelper<TestPortProviderNode>>(workSemaphore: 1)]
public partial class TestPortProviderNode
{
    public TestPortProviderNode() => InitializeWorkflow();

    /// <summary>A non-enum provider whose type the archive does not carry.</summary>
    [VeloxProperty]
    [SlotSelectors(typeof(NotArchivablePortProvider))]
    public partial SlotEnumerator<SlotDefaultViewModel> Ports { get; set; }

    /// <summary>A whitelist naming an enum the tree holds only as a reference.</summary>
    [VeloxProperty]
    [SlotSelectors(typeof(TestRouteKind))]
    public partial SlotEnumerator<SlotDefaultViewModel> Kinds { get; set; }

    /// <summary>A whitelist naming an enum the tree holds as a type.</summary>
    [VeloxProperty]
    [SlotSelectors(typeof(TestPortKind))]
    public partial SlotEnumerator<SlotDefaultViewModel> Modes { get; set; }

    /// <summary>No whitelist at all — the only shape the <c>bool</c> route is reachable from.</summary>
    [VeloxProperty]
    public partial SlotEnumerator<SlotDefaultViewModel> Any { get; set; }
}

/// <summary>
/// <c>SetEnumSlotCollection</c> — the only route that reshapes a node's ports, and the route the Agent's own
/// instructions point at.
/// </summary>
/// <remarks>
/// It had <b>no test at all</b> before this file, on either path, which is how the demo's Python nodes spent the
/// migration unable to have their ports rebuilt by an Agent: the tool read the provider back through the archive
/// serializer, and the archive serializer had never been told about the provider's type. Driven through the public
/// tool surface (see <see cref="WorkflowToolInvoker"/>) rather than by calling the toolkit's body.
/// </remarks>
[TestClass]
public class SetEnumSlotCollectionTests
{
    private static string Set(WorkflowAgentScope scope, string propertyName, string selectorTypeOrJson,
        string? nonEnumTypeName = null)
        => WorkflowToolInvoker.Invoke(scope, "SetEnumSlotCollection",
            ("nodeIndex", 0),
            ("propertyName", propertyName),
            ("selectorTypeOrJson", selectorTypeOrJson),
            ("nonEnumTypeName", nonEnumTypeName));

    private static JObject ErrorOf(string result)
    {
        var json = JObject.Parse(result);
        Assert.AreEqual("error", json["status"]?.Value<string>(), result);
        return json;
    }

    private static string MessageOf(string result) => ErrorOf(result)["message"]!.Value<string>()!;

    /// <summary>
    /// The reported defect, end to end: the demo's Python node has its ports rebuilt from the provider's JSON.
    /// </summary>
    /// <remarks>
    /// The demo itself never came through here — <c>WorkflowDemoSession</c> calls <c>SetSelector</c> in process —
    /// so nothing noticed that the Agent's route to the same method could not run at all.
    /// </remarks>
    [TestMethod]
    public void TheNonEnumProviderPath_RebuildsThePythonNodesPorts()
    {
        var tree = new TreeDefaultViewModel();
        var node = new PythonScriptNodeViewModel();
        tree.GetHelper().CreateNode(node);

        var result = JObject.Parse(Set(new WorkflowAgentScope(tree), "InputSlots",
            """{"Ports":[{"Name":"stats"},{"Name":"dist"},{"Name":"anomalies"}]}""",
            typeof(PythonPortProvider).FullName!));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.AreEqual(typeof(PythonPortProvider).FullName, result["selectorType"]?.Value<string>(),
            "the result names the type it actually installed");

        // The result has to report what it built, or the caller has no way to confirm the call did anything —
        // and a model that cannot confirm re-sends, which is exactly what a live run showed it doing.
        Assert.AreEqual(3, result["count"]?.Value<int>(), "the result reports how many ports it built");
        CollectionAssert.AreEqual(
            new[] { "stats", "dist", "anomalies" },
            result["slots"]!.Select(s => s!["label"]!.Value<string>()).ToArray(),
            "and which ones, by the label each carries");

        Assert.HasCount(3, node.InputSlots.Items, "one slot per port the provider declared");
        CollectionAssert.AreEqual(
            new[] { "stats", "dist", "anomalies" },
            node.InputSlots.Items.Select(i => i.Value?.ToString()).ToArray(),
            "the port names are the routing keys, in the order the provider gave them");
    }

    /// <summary>
    /// Reshaping the same node's ports a second time applies the new set — the whole point of the tool.
    /// </summary>
    /// <remarks>
    /// The provider's type does not change between the two calls, and <c>SlotEnumerator.SetSelector</c> keeps one
    /// remembered <c>SelectorState</c> per selector <i>type name</i>. Re-setting the same type therefore found a
    /// remembered state, restored it, and dropped the freshly built slots on the floor — reporting success for a
    /// no-op. The framework already knew: <c>PythonScriptNodeViewModel</c> leaves its ports unset in the
    /// constructor for exactly this reason. What nobody had noticed is that the same rule makes the Agent unable
    /// to reshape a node that already has ports, which is the only thing this tool exists to do.
    /// </remarks>
    [TestMethod]
    public void ReshapingThePortsASecondTime_AppliesTheNewSet()
    {
        var tree = new TreeDefaultViewModel();
        var node = new PythonScriptNodeViewModel();
        tree.GetHelper().CreateNode(node);

        var scope = new WorkflowAgentScope(tree);
        var provider = typeof(PythonPortProvider).FullName!;

        Set(scope, "InputSlots", """{"Ports":[{"Name":"a"}]}""", provider);
        Assert.HasCount(1, node.InputSlots.Items, "precondition: the first reshape lands");

        var second = JObject.Parse(Set(scope, "InputSlots",
            """{"Ports":[{"Name":"a"},{"Name":"b"}]}""", provider));

        Assert.AreEqual("ok", second["status"]?.Value<string>(), second.ToString());
        Assert.HasCount(2, node.InputSlots.Items,
            "the second reshape must reach the enumerator — a tool that reports success for a no-op is worse "
            + "than one that fails, because the caller has no reason to look again");
    }

    /// <summary>
    /// A provider with no archive reader is refused with the fix in the message, not the engine's own wording.
    /// </summary>
    /// <remarks>
    /// The engine's message ("has no registered JSON reader") is true and useless: it names no route out. The
    /// model that hit it went looking for a host registration that does not exist. The type is in the context
    /// tree and resolvable — what is missing is <c>[Archivable]</c>, and that is what the answer must say.
    /// </remarks>
    [TestMethod]
    public void ANonEnumProviderWithNoReader_SaysToAddArchivable()
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new TestPortProviderNode());

        var message = MessageOf(Set(new WorkflowAgentScope(tree), "Ports",
            """{"Names":["a"]}""", typeof(NotArchivablePortProvider).FullName!));

        StringAssert.Contains(message, "Archivable", "the message must name the annotation that fixes it");
        StringAssert.Contains(message, typeof(NotArchivablePortProvider).FullName!,
            "and the type the caller asked for");
        Assert.IsFalse(message.Contains("JSON reader", System.StringComparison.OrdinalIgnoreCase),
            "the engine's own wording is the one thing it must not fall back to — it sends the reader looking for a host registration that does not exist");
    }

    /// <summary>The enum route materialises one slot per member, in declaration order.</summary>
    [TestMethod]
    public void TheEnumPath_CreatesOneSlotPerMember()
    {
        var tree = new TreeDefaultViewModel();
        var node = new TestPortProviderNode();
        tree.GetHelper().CreateNode(node);

        var result = JObject.Parse(Set(new WorkflowAgentScope(tree), "Modes", typeof(TestPortKind).FullName!));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.AreEqual(2, result["count"]?.Value<int>(), "one slot per enum member");
        CollectionAssert.AreEqual(
            System.Enum.GetNames(typeof(TestPortKind)),
            node.Modes.Items.Select(i => i.Value?.ToString()).ToArray());
    }

    /// <summary>
    /// An enum <c>[SlotSelectors]</c> names but nothing else does is <b>not</b> resolvable, and the refusal says so.
    /// </summary>
    /// <remarks>
    /// This is the sharp edge of the closed world, and it is not obvious from the attribute: naming a type in
    /// <c>[SlotSelectors]</c> puts a <i>reference</i> into the context tree, but no type entry and no accessor —
    /// and <c>AgentTypeResolver</c> resolves through accessors. So a selector enum that carries no
    /// <c>[AgentContext]</c> and is not the declared type of some member cannot be selected by the Agent at all,
    /// even though the property it is attached to lists it as allowed. The demo's own enums carry the attribute;
    /// a consuming project has to know to do the same.
    /// </remarks>
    [TestMethod]
    public void AnEnumTheTreeHoldsOnlyAsAReference_DoesNotResolve()
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new TestPortProviderNode());

        var message = MessageOf(Set(new WorkflowAgentScope(tree), "Kinds", typeof(TestRouteKind).FullName!));

        StringAssert.Contains(message, typeof(TestRouteKind).FullName!);
        StringAssert.Contains(message, "not found");
    }

    /// <summary>The <c>bool</c> route is the enum route with two members — reachable only with no whitelist.</summary>
    [TestMethod]
    public void TheBooleanPath_CreatesFalseAndTrue()
    {
        var tree = new TreeDefaultViewModel();
        var node = new TestPortProviderNode();
        tree.GetHelper().CreateNode(node);

        var result = JObject.Parse(Set(new WorkflowAgentScope(tree), "Any", "System.Boolean"));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        CollectionAssert.AreEqual(new[] { "False", "True" },
            node.Any.Items.Select(i => i.Value?.ToString()).ToArray());
    }

    /// <summary>A <c>[SlotSelectors]</c> whitelist is enforced, and the refusal lists what is allowed.</summary>
    [TestMethod]
    public void ATypeOffTheWhitelist_IsRefusedWithTheAllowedList()
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new TestPortProviderNode());

        // Resolvable (it carries [AgentContext]) but not the one this property allows.
        var message = MessageOf(Set(new WorkflowAgentScope(tree), "Kinds", typeof(TestPortKind).FullName!));

        StringAssert.Contains(message, "not allowed");
        StringAssert.Contains(message, typeof(TestRouteKind).FullName!,
            "the refusal has to say what would have been accepted, or the model can only guess");
    }

    /// <summary>An unknown property, and a resolvable type that is neither enum nor bool, are both named.</summary>
    [TestMethod]
    public void AnUnknownPropertyOrAWrongKindOfType_IsRefusedByName()
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new TestPortProviderNode());

        var scope = new WorkflowAgentScope(tree);

        StringAssert.Contains(MessageOf(Set(scope, "NoSuchProperty", "System.Boolean")), "NoSuchProperty");

        StringAssert.Contains(MessageOf(Set(scope, "Modes", typeof(NotArchivablePortProvider).FullName!)),
            "not an enum or bool",
            "a non-enum type name without nonEnumTypeName gets the hint that would have made it work");
    }

    /// <summary>A property that is not a <c>SlotEnumerator</c> is refused as such.</summary>
    [TestMethod]
    public void APlainProperty_IsRefusedAsNotAnEnumerator()
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new PythonScriptNodeViewModel());

        StringAssert.Contains(MessageOf(Set(new WorkflowAgentScope(tree), "Title", "System.Boolean")),
            "not a SlotEnumerator");
    }
}
