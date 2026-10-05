using System.Linq;
using Demo.ViewModels;
using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// A component the author declared is a component the Agent can find.
/// </summary>
/// <remarks>
/// A node written the documented way — <c>[WorkflowBuilder.Node&lt;T&gt;]</c> and nothing else — carries no
/// <c>[AgentContext]</c> anywhere. The archive side already treats that attribute as "this is a workflow
/// component"; the agent-context side knew only the interface, which the Workflow generator injects and the
/// catalog generator therefore cannot see.
/// </remarks>
[WorkflowBuilder.Node<NodeHelper<UnannotatedNode>>(workSemaphore: 1)]
public partial class UnannotatedNode
{
    public UnannotatedNode() => InitializeWorkflow();
}

/// <summary>A plain payload class — no <c>[AgentContext]</c>, not a component, not an enum or a struct.</summary>
public sealed class PlainPayload
{
    /// <summary>Something the model would want described.</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>A component that exposes <see cref="PlainPayload"/> through an annotated member.</summary>
[WorkflowBuilder.Node<NodeHelper<PayloadHolderNode>>(workSemaphore: 1)]
public partial class PayloadHolderNode
{
    public PayloadHolderNode() => InitializeWorkflow();

    [AgentContext(AgentLanguages.English, "The payload this node works on.")]
    [VeloxProperty] private PlainPayload payload = new();
}

[TestClass]
public class DiscoveryCoverageTests
{
    private static WorkflowAgentScope ScopeWith(params object[] nodes)
    {
        var tree = new TreeDefaultViewModel();
        foreach (var node in nodes) tree.GetHelper().CreateNode((IWorkflowNodeViewModel)node);
        return new WorkflowAgentScope(tree);
    }

    /// <summary>A declared component is resolvable by name, with or without annotations.</summary>
    [TestMethod]
    public void AComponentDeclaredOnlyWithWorkflowBuilder_IsResolvable()
    {
        var resolved = AgentTypeResolver.ResolveType(typeof(UnannotatedNode).FullName!);

        Assert.IsNotNull(resolved,
            "a node declared with [WorkflowBuilder.Node<T>] is a workflow component — the same attribute the archive "
            + "side accepts as the proof. The agent catalog cannot test the interface it would otherwise use, "
            + "because another generator injects that interface in the same pass");
        Assert.AreEqual(typeof(UnannotatedNode), resolved);
    }

    /// <summary>And the Agent can create one, which is the thing the catalog exists to enable.</summary>
    [TestMethod]
    public void AComponentDeclaredOnlyWithWorkflowBuilder_IsCreatableByTheAgent()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        var created = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(UnannotatedNode).FullName!)));

        Assert.AreEqual("ok", created["status"]?.Value<string>(), created.ToString());
    }

    /// <summary>
    /// A class a member exposes is addressable, so the schema can describe what the listing just showed.
    /// </summary>
    /// <remarks>
    /// The catalog's second pass exists so the Agent never reads a field's type name it cannot then look up — but
    /// it lifted only the enums and the structs, so the most ordinary payload of all, a plain class, fell through.
    /// The archive side has no such limit: it walks declared member types for everything reachable.
    /// </remarks>
    [TestMethod]
    public void AClassExposedByAnAnnotatedMember_IsResolvable()
    {
        var resolved = AgentTypeResolver.ResolveType(typeof(PlainPayload).FullName!);

        Assert.IsNotNull(resolved,
            "a payload class the model is shown the name of must be one it can ask about");
        Assert.AreEqual(typeof(PlainPayload), resolved);
    }

    /// <summary>
    /// A <c>SlotEnumerator</c> with no <c>[SlotSelectors]</c> accepts any selector — the whitelist is what
    /// restricts, and there is none.
    /// </summary>
    /// <remarks>
    /// Worth pinning because the opposite reading is just as plausible from the outside: an enumerator whose
    /// provider is not declared could be taken for one that accepts nothing. It is the other way round — the
    /// attribute is the restriction, so its absence is permission. Both routes are checked, because the enum
    /// and the provider route test the whitelist in separate code.
    /// </remarks>
    [TestMethod]
    public void AnEnumeratorWithNoSlotSelectors_AcceptsAnySelector()
    {
        var node = new TestPortProviderNode();   // Any — a SlotEnumerator with no [SlotSelectors]
        var scope = ScopeWith(node);

        var boolean = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "SetEnumSlotCollection",
            ("nodeIndex", 0), ("propertyName", "Any"), ("selectorTypeOrJson", "System.Boolean")));
        Assert.AreEqual("ok", boolean["status"]?.Value<string>(), "an enum/bool selector needs no whitelist: " + boolean);

        // An archivable provider, so a failure here is the whitelist talking and nothing else.
        var provider = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "SetEnumSlotCollection",
            ("nodeIndex", 0), ("propertyName", "Any"),
            ("selectorTypeOrJson", """{"Ports":[{"Name":"a"},{"Name":"b"}]}"""),
            ("nonEnumTypeName", typeof(PythonPortProvider).FullName!)));
        Assert.AreEqual("ok", provider["status"]?.Value<string>(),
            "and neither does a provider — one that no [SlotSelectors] anywhere names: " + provider);
        Assert.HasCount(2, node.Any.Items);
    }
}
