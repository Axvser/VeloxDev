using System.Linq;
using Demo.ViewModels;
using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>A payload whose annotated member is a public field — the shape only the data table lists.</summary>
/// <remarks>
/// A public field lands in the catalog's <c>Fields</c> directory, and the class table reads <c>Properties</c>
/// only, so this member is reachable through exactly one of the two renderings of a type entry.
/// </remarks>
public sealed class FieldPayload
{
    /// <summary>Something the Agent must be told about, on a field rather than a property.</summary>
    [AgentContext(AgentLanguages.English, "A payload field the Agent must be told about.")]
    public string Note = string.Empty;
}

/// <summary>A node that exposes <see cref="FieldPayload"/> through an annotated member.</summary>
[WorkflowBuilder.Node<NodeHelper<FieldPayloadHolderNode>>(workSemaphore: 1)]
public partial class FieldPayloadHolderNode
{
    public FieldPayloadHolderNode() => InitializeWorkflow();

    [AgentContext(AgentLanguages.English, "The field payload this node works on.")]
    [VeloxProperty] private FieldPayload payload = new();
}

/// <summary>
/// The two discovery tools that had no coverage at all: the creatable-type listing, and the per-type context
/// block the skeleton's mandate sends the model to before it touches anything.
/// </summary>
/// <remarks>
/// Both answer from the compiled context tree, so what they report is the whole of what the model can know about
/// a type it was told the name of. Each test says which annotation put the type in front of the model in the
/// first place, because that is the half that goes wrong silently: a declaration route the catalog generator
/// does not recognise produces no error anywhere, only a type the Agent cannot find.
/// </remarks>
[TestClass]
public class ComponentContextDiscoveryTests
{
    private static WorkflowAgentScope Scope() => new(new TreeDefaultViewModel());

    private static string Context(string fullTypeName, string language = "English")
        => WorkflowToolInvoker.Invoke(Scope(), "GetComponentContext",
            ("fullTypeName", fullTypeName), ("language", language));

    private static JObject CreatableTypes() => JObject.Parse(WorkflowToolInvoker.Invoke(Scope(), "ListCreatableTypes"));

    private static string[] Names(JObject listed, string key)
        => [.. listed[key]!.Select(entry => entry!["fullName"]!.Value<string>()!)];

    // ── GetComponentContext ─────────────────────────────────────────────────

    /// <summary>A component declared with <c>[WorkflowBuilder.Node&lt;T&gt;]</c> gets the class table.</summary>
    [TestMethod]
    public void GetComponentContext_RendersAClassBlockForAComponent()
    {
        var block = Context(typeof(PayloadHolderNode).FullName!);

        StringAssert.Contains(block, "Class");
        StringAssert.Contains(block, "Properties:");
        // 提升出来的属性是第一列用声明类型、第二列用提升名的形状 —— 逐字钉住，模型读的就是这两列。
        StringAssert.Contains(block, $"| {typeof(PlainPayload).FullName} | Payload |");
        StringAssert.Contains(block, "The payload this node works on.");
    }

    /// <summary>An enum gets the enum table, with each member's value.</summary>
    [TestMethod]
    public void GetComponentContext_RendersAnEnumBlockForAnEnum()
    {
        var block = Context(typeof(TestPortKind).FullName!);

        StringAssert.Contains(block, "Enum");
        StringAssert.Contains(block, "| Name | Value | Description |");
        StringAssert.Contains(block, $"| {nameof(TestPortKind.Primary)} | {(int)TestPortKind.Primary} |");
        StringAssert.Contains(block, "Test-only selector enum");
    }

    /// <summary>An interface gets the interface table, whose commands carry the parameter type they declared.</summary>
    [TestMethod]
    public void GetComponentContext_RendersAnInterfaceBlockForAnInterface()
    {
        var block = Context(typeof(IWorkflowNodeViewModel).FullName!);

        StringAssert.Contains(block, "Interface");
        StringAssert.Contains(block, "| Name | ParameterType | Description |");
        StringAssert.Contains(block, $"| {nameof(IWorkflowNodeViewModel.MoveCommand)} | {typeof(Offset).FullName} |",
            "the parameter type the interface declared is the column the model builds its JSON from");
    }

    /// <summary>
    /// A data type — <c>Anchor</c> and its kin — is described as data, not as a component.
    /// </summary>
    /// <remarks>
    /// The module renders a type entry by its catalog <c>Kind</c> everywhere else
    /// (<c>ProvideFrameworkDataContext</c>, <c>WithData</c>); this tool was the one place that picked the
    /// rendering off the CLR shape and so gave a data entry the class table, including a command table no value
    /// object has. Both tables list <c>Anchor</c>'s members, so the heading is what tells the two apart.
    /// </remarks>
    [TestMethod]
    public void GetComponentContext_RendersADataBlockForADataType()
    {
        var block = Context(typeof(Anchor).FullName!);

        StringAssert.Contains(block, "Data Type");
        StringAssert.Contains(block, "Fields / Properties:");
        Assert.IsFalse(block.Contains("Developer Instructions"),
            "a value object has no commands and no authoritative developer defaults — the class table would claim it does: " + block);
    }

    /// <summary>
    /// And the difference is not only a heading: the data table is the one that lists a member declared as a
    /// field, so the class rendering silently drops it.
    /// </summary>
    [TestMethod]
    public void GetComponentContext_OnADataType_ListsAMemberTheClassTableWouldDrop()
    {
        // 前提：这个类型确实在目录里、且确实是被标注过的成员带进来的 —— 否则下面那条断言只是在说「查无此型」。
        Assert.IsNotNull(AgentTypeResolver.ResolveType(typeof(FieldPayload).FullName!),
            "precondition: the annotated field is what puts this payload in the catalog");

        var block = Context(typeof(FieldPayload).FullName!);

        StringAssert.Contains(block, $"| {typeof(string).FullName} | {nameof(FieldPayload.Note)} |",
            "a member the Agent is told about must be one it can then read back: " + block);
        StringAssert.Contains(block, "A payload field the Agent must be told about.");
    }

    /// <summary>An annotated field is listed once, not once per table that could claim it.</summary>
    /// <remarks>
    /// <c>[AgentContext]</c> on a <c>[VeloxProperty]</c> field is the pattern every demo node uses for its own
    /// settings, and the class table has two passes that can both claim such a member — one for promoted fields,
    /// one for properties carrying the attribute. The reflective table this reproduces has only the first, since
    /// the attribute sits on the field and the generated property never carries it. A consumer reading this
    /// block therefore sees each setting twice, which is a cost paid on every turn.
    /// </remarks>
    [TestMethod]
    public void GetComponentContext_ListsAnAnnotatedFieldOnce()
    {
        // 前提：这个类型确实带着「字段上写标注」的成员，否则下面的计数只是 0 对 0。
        var type = typeof(TimerNodeViewModel);
        Assert.IsNotNull(AgentTypeResolver.ResolveType(type.FullName!), "precondition: the demo node is in the catalog");

        var block = Context(type.FullName!);

        foreach (var member in new[] { "Title", "IntervalMilliseconds", "LastTick" })
        {
            Assert.AreEqual(1, RowCount(block, member),
                $"'{member}' must appear exactly once in the property table:\n{block}");
        }
    }

    // 数「名字那一列等于 memberName」的行 —— 说明文字里偶然出现的同名不算。
    private static int RowCount(string block, string memberName)
        => block.Split('\n').Count(line => line.Contains($"| {memberName} |"));

    /// <summary>A name the tree does not carry is reported as such rather than rendered empty.</summary>
    [TestMethod]
    public void GetComponentContext_ForAnUnknownType_ReportsAnError()
    {
        var result = JObject.Parse(Context("System.String"));

        Assert.AreEqual("error", result["status"]?.Value<string>(), result.ToString());
        StringAssert.Contains(result["message"]?.Value<string>() ?? string.Empty, "System.String");
    }

    /// <summary>The requested language is honoured, and a member annotated in one language only falls back.</summary>
    /// <remarks>
    /// <c>Anchor</c> carries both languages, so it answers each request with its own text. <c>PayloadHolderNode</c>
    /// carries English only, so the Chinese request has to fall back rather than render an empty description —
    /// the model reading a Chinese session must not lose a member that only has an English sentence.
    /// </remarks>
    [TestMethod]
    public void GetComponentContext_FollowsTheRequestedLanguage()
    {
        var chinese = Context(typeof(Anchor).FullName!, "Chinese");
        StringAssert.Contains(chinese, "用于在工作流系统中描述组件的空间位置");
        Assert.IsFalse(chinese.Contains("Used to describe the spatial position"),
            "an exact-language match is used outright, never mixed with the other language");

        var english = Context(typeof(PayloadHolderNode).FullName!, "English");
        var fallback = Context(typeof(PayloadHolderNode).FullName!, "Chinese");
        StringAssert.Contains(english, "The payload this node works on.");
        StringAssert.Contains(fallback, "The payload this node works on.",
            "an English-only annotation still answers a Chinese request");
    }

    // ── ListCreatableTypes ──────────────────────────────────────────────────

    /// <summary>Every declaration route the catalog knows puts the component in the listing.</summary>
    [TestMethod]
    public void ListCreatableTypes_ListsTheComponentsDeclaredTheDocumentedWay()
    {
        var listed = CreatableTypes();
        var nodes = Names(listed, "nodeTypes");
        var slots = Names(listed, "slotTypes");

        // 自证守卫：目录空掉、根段改名、组件段整体丢失，都要红在这一条上，而不是让下面几条空过。
        Assert.IsNotEmpty(nodes, "the framework's own node components must be listed: " + listed);
        Assert.IsNotEmpty(slots, "and its slot components: " + listed);

        // 接口那一档（框架自己的四个组件由实现接口识别）。
        CollectionAssert.Contains(nodes, typeof(NodeDefaultViewModel).FullName);
        CollectionAssert.Contains(slots, typeof(SlotDefaultViewModel).FullName);

        // [WorkflowBuilder.*] 那一档（消费方唯一会写下的声明），Node 与 Slot 各验一次。
        CollectionAssert.Contains(nodes, typeof(UnannotatedNode).FullName);
        CollectionAssert.Contains(slots, typeof(ProbeSlot).FullName);
    }

    /// <summary>A type the catalog carries but that is not a component is not offered as creatable.</summary>
    [TestMethod]
    public void ListCreatableTypes_OmitsTypesThatAreNotComponents()
    {
        Assert.IsNotNull(AgentTypeResolver.ResolveType(typeof(PlainPayload).FullName!),
            "precondition: the reachability pass does put this payload in the catalog, so absence below is about "
            + "the component test and not about the type being unknown");

        var listed = CreatableTypes();

        CollectionAssert.DoesNotContain(Names(listed, "nodeTypes"), typeof(PlainPayload).FullName);
        CollectionAssert.DoesNotContain(Names(listed, "slotTypes"), typeof(PlainPayload).FullName);
    }

    /// <summary>Links and trees are components, but no tool creates one from a type name.</summary>
    /// <remarks>
    /// Pinned because the listing is what the model reads as "these are the types I may build". A link type
    /// appearing here would send it to <c>CreateNode</c> with a link's name.
    /// </remarks>
    [TestMethod]
    public void ListCreatableTypes_OmitsLinksAndTrees()
    {
        foreach (var type in new[] { typeof(LinkViewModel), typeof(TreeViewModel) })
        {
            Assert.IsNotNull(AgentTypeResolver.ResolveType(type.FullName!),
                $"precondition: {type.Name} is a component the catalog carries");
        }

        var listed = CreatableTypes();

        CollectionAssert.DoesNotContain(Names(listed, "nodeTypes"), typeof(LinkViewModel).FullName);
        CollectionAssert.DoesNotContain(Names(listed, "slotTypes"), typeof(TreeViewModel).FullName);
    }
}
