using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Demo.ViewModels;
using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
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

/// <summary>A slot component declared the documented way, and nothing else.</summary>
/// <remarks>
/// The four <c>[WorkflowBuilder.*]</c> attributes are the only component declaration a consuming project writes;
/// the <c>IWorkflow*ViewModel</c> interface that would otherwise identify the component is injected by another
/// generator in the same pass, so a generator that scans for the interface cannot see it. Only a
/// <c>[WorkflowBuilder.Node]</c> fixture existed before this one, which left three of the four declaration
/// routes unguarded.
/// </remarks>
[WorkflowBuilder.Slot<SlotHelper>]
public partial class ProbeSlot
{
    public ProbeSlot() => InitializeWorkflow();
}

/// <summary>An enum nothing annotates — it is reachable only through the member that declares it.</summary>
public enum ReachableGrade
{
    /// <summary>The lower grade.</summary>
    Low,

    /// <summary>The higher grade.</summary>
    High,
}

/// <summary>A component that declares <see cref="ReachableGrade"/> as a member's type.</summary>
/// <remarks>
/// The catalog's second pass exists so the Agent never reads a type name it cannot then look up. An enum with no
/// <c>[AgentContext]</c> anywhere is the case that depends on it entirely.
/// </remarks>
[WorkflowBuilder.Node<NodeHelper<ReachableEnumHolderNode>>(workSemaphore: 1)]
public partial class ReachableEnumHolderNode
{
    public ReachableEnumHolderNode() => InitializeWorkflow();

    [AgentContext(AgentLanguages.English, "The grade this holder selects.")]
    [VeloxProperty] private ReachableGrade grade = ReachableGrade.Low;
}

/// <summary>The member shapes a component can declare, each of which has to be discovered its own way.</summary>
[WorkflowBuilder.Node<NodeHelper<MemberShapeProbeNode>>(workSemaphore: 1)]
public partial class MemberShapeProbeNode
{
    public MemberShapeProbeNode() => InitializeWorkflow();

    /// <summary>A promoted field with no <c>[AgentContext]</c> — described by no table, present in the schema.</summary>
    [VeloxProperty] private string bare = string.Empty;

    /// <summary>The underscore-prefixed spelling of the same promotion rule.</summary>
    [AgentContext(AgentLanguages.English, "An underscore-named field, annotated.")]
    [VeloxProperty] private int _volume;

    /// <summary>Renames this probe; the parameter type is what the Agent has to construct.</summary>
    [VeloxCommand]
    [AgentContext(AgentLanguages.English, "Renames the probe.")]
    [AgentCommandParameter(typeof(string))]
    public Task Rename(object? parameter, CancellationToken ct) => Task.CompletedTask;
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

    /// <summary>Every <c>[WorkflowBuilder.*]</c> declaration routes the type into its own component directory.</summary>
    /// <remarks>
    /// Four attributes, four directories, and the mapping is decided by which attribute the author wrote — not by
    /// the interface, which a different generator injects in the same pass. That is why each arm needs its own
    /// fixture rather than one representative: an arm the catalog generator does not recognise produces no error,
    /// only a component the Agent cannot find.
    /// </remarks>
    [TestMethod]
    public void EveryWorkflowBuilderDeclaration_RoutesToItsOwnComponentDirectory()
    {
        var routes = new (Type Type, string Segment)[]
        {
            (typeof(UnannotatedNode), "Nodes"),          // 本工程声明，[WorkflowBuilder.Node<T>]
            (typeof(ProbeSlot), "Slots"),                // 本工程声明，[WorkflowBuilder.Slot<T>]
            (typeof(SlotViewModel), "Slots"),            // Lib 声明，非泛型 helper 形式
            (typeof(LinkViewModel), "Links"),
            (typeof(TreeViewModel), "Trees"),
        };

        foreach (var (type, segment) in routes)
        {
            var path = AIContextTreeRegistry.PathFor(type.FullName!);
            Assert.IsNotNull(path,
                $"{type.Name} declares itself with [WorkflowBuilder.*], which is the only proof of componenthood "
                + "the catalog generator can see");

            StringAssert.Contains(path, $"/Components/{segment}/",
                $"{type.Name} belongs in the {segment} directory: {path}");
        }

        // 自证守卫：四段都得出现过，否则一条把某段拼错的改动会让上面几条一起空过。
        foreach (var segment in new[] { "Nodes", "Slots", "Links", "Trees" })
        {
            Assert.IsTrue(routes.Any(r => r.Segment == segment),
                $"the walk never covered the {segment} directory, so it is no longer looking at all four arms");
        }
    }

    /// <summary>An enum nothing annotates is still found, because a member declares it.</summary>
    /// <remarks>
    /// The catalog's second pass exists so the Agent never reads a type name it cannot then look up. An enum with
    /// no <c>[AgentContext]</c> anywhere depends on that pass entirely — the same shape as
    /// <see cref="TestRouteKind"/>, which is named only by <c>[SlotSelectors]</c> and therefore does <i>not</i>
    /// resolve.
    /// </remarks>
    [TestMethod]
    public void AnEnumReachableOnlyAsAMemberType_IsResolvable()
    {
        var declared = typeof(ReachableEnumHolderNode)
            .GetField("grade", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(declared, "precondition: the holder still declares the member that makes the enum reachable");
        Assert.AreEqual(typeof(ReachableGrade), declared!.FieldType);

        Assert.IsNotNull(AgentTypeResolver.ResolveType(typeof(ReachableGrade).FullName!),
            "an enum the Agent is shown as a property type must be one it can ask about");
    }

    /// <summary>A <c>[VeloxProperty]</c> field is stored under the property name it is promoted to.</summary>
    /// <remarks>
    /// The name is what every other tool keys on — <c>PatchNodeProperties</c> writes by it, the schema lists it —
    /// so a member the Agent cannot name is a member it cannot set.
    /// </remarks>
    [TestMethod]
    public void APromotedField_IsNamedByThePromotionRule()
    {
        var properties = AIContextDirectory.Shared.MembersAcross(typeof(MemberShapeProbeNode).FullName!, "Properties");

        // 两种拼法：无前缀，以及带一个下划线的（下划线要被吃掉）。
        Assert.Contains(p => p.Name == "Bare" && p.Has(AIContextFlags.IsPromotedField), properties,
            "a field named 'bare' is addressable as 'Bare'");
        Assert.Contains(p => p.Name == "Volume" && p.Has(AIContextFlags.IsPromotedField), properties,
            "a field named '_volume' is addressable as 'Volume'");
    }

    /// <summary>A promoted field's <c>[AgentContext]</c> is readable from the field the author annotated.</summary>
    /// <remarks>
    /// The entry is stored under the promoted name, so a lookup by the member's own name finds nothing — and a
    /// caller holding the <see cref="System.Reflection.FieldInfo"/> has nothing else to go on. Every table keeps
    /// its rows because the renderers read the tree directly; this is the one route that goes through the member,
    /// which is why the reflection oracle could never see a promoted member's text at all.
    /// </remarks>
    [TestMethod]
    public void APromotedFieldsAnnotation_IsReadableFromTheField()
    {
        var field = typeof(PayloadHolderNode)
            .GetField("payload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(field, "precondition: the holder still declares the promoted field");
        Assert.IsTrue(field!.IsDefined(typeof(VeloxPropertyAttribute), inherit: false),
            "precondition: [VeloxProperty] is what makes this the promoted shape");

        var descriptions = AgentContextCollector.GetAgentContext(field, AgentLanguages.English);

        Assert.HasCount(1, descriptions, "the field carries exactly one English sentence");
        Assert.AreEqual("The payload this node works on.", descriptions[0]);
    }

    /// <summary>
    /// An annotated field without <c>[AgentContext]</c> is in the schema but in no rendered table.
    /// </summary>
    /// <remarks>
    /// Worth pinning because the two answer different questions — the schema is "what can I set", the rendered
    /// table is "what was the author willing to explain" — and a change that made one follow the other would be
    /// silent either way.
    /// </remarks>
    [TestMethod]
    public void APromotedFieldWithoutAgentContext_IsInTheSchemaOnly()
    {
        var schema = TypeIntrospector.GetTypeSchema(typeof(MemberShapeProbeNode));
        StringAssert.Contains(schema, "\"Bare\"",
            "the schema lists what the Agent may write, described or not");

        var scope = ScopeWith(new MemberShapeProbeNode());
        var block = WorkflowToolInvoker.Invoke(scope, "GetComponentContext",
            ("fullTypeName", typeof(MemberShapeProbeNode).FullName!), ("language", "English"));

        StringAssert.Contains(block, "Volume", "precondition: the annotated sibling does get a row");
        Assert.IsFalse(block.Contains("Bare"),
            "an undescribed member is not given a row in the table the model reads:\n" + block);
    }

    /// <summary>A <c>[VeloxCommand]</c> method is reachable by the command name, with its parameter type.</summary>
    [TestMethod]
    public void ACommand_IsDiscoverableWithItsParameterType()
    {
        var node = new MemberShapeProbeNode();
        var scope = ScopeWith(node);

        var listed = JArray.Parse(WorkflowToolInvoker.Invoke(scope, "ListComponentCommands", ("nodeIndex", 0)));
        var command = listed.SingleOrDefault(c => c!["n"]?.Value<string>() == "RenameCommand");
        Assert.IsNotNull(command, "the promoted command property is what the Agent invokes: " + listed);
        Assert.AreEqual(nameof(String), command!["p"]?.Value<string>(),
            "and the type [AgentCommandParameter] declared is the column it builds its JSON from");

        var discovered = CommandInvoker.DiscoverCommands(node)
            .Single(c => c.Name == "RenameCommand");
        Assert.AreEqual(typeof(string), discovered.ParameterType,
            "the descriptor the toolkit runs from carries the same type as a literal");
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
