using System.Text.Json.Nodes;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The type schema the agent reads before it constructs anything.
/// </summary>
/// <remarks>
/// The schema is built from the context tree and the type's generated accessor, so these pin both the shape of
/// the JSON and the closed world it answers from.
/// </remarks>
[TestClass]
public class TypeIntrospectorTests
{
    [TestMethod]
    public void AnEnumSchemaCarriesItsMembersAndValues()
    {
        var schema = JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(SlotChannel)))!;

        Assert.AreEqual("enum", schema["kind"]?.GetValue<string>());
        Assert.AreEqual(typeof(SlotChannel).FullName, schema["fullName"]?.GetValue<string>());

        var values = schema["values"]!.AsObject();
        Assert.AreEqual((long)SlotChannel.OneSource, values[nameof(SlotChannel.OneSource)]!.GetValue<long>());
    }

    [TestMethod]
    public void AComponentSchemaCarriesItsPropertiesWithFriendlyTypeNames()
    {
        var schema = JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(TreeDefaultViewModel)))!;

        Assert.AreEqual("class", schema["kind"]?.GetValue<string>());

        var properties = schema["properties"]!.AsArray();
        var names = properties.Select(p => p!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("Nodes", names);

        var nodes = properties.First(p => p!["name"]!.GetValue<string>() == "Nodes")!;
        Assert.AreEqual(
            "ObservableCollection<VeloxDev.WorkflowSystem.IWorkflowNodeViewModel>",
            nodes["type"]?.GetValue<string>(),
            "only the special types are spelled short; a type argument keeps its full name");
        Assert.IsTrue(nodes["canRead"]!.GetValue<bool>());
    }

    [TestMethod]
    public void ADataTypeIsReportedAsAStruct()
    {
        // Anchor / Offset / Size 名字像值对象，其实是 class；CellKey 才是真的 struct。
        Assert.AreEqual("class", JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(Anchor)))!["kind"]?.GetValue<string>());

        var schema = JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(CellKey)))!;
        Assert.AreEqual("struct", schema["kind"]?.GetValue<string>());

        // 值类型恒能无参构造，所以默认实例那条路对它同样成立 —— 前提是这个类型在存档闭包里。
        // CellKey 在目录里（所以上面报得出 struct），但没有任何文档会写到它，因此生成器没为它发条目。
        Assert.IsNull(schema["defaultJson_runtimeOnly"], "a type outside the archive closure has no default instance to serialize");

        // 收录过的类型才拿得出默认实例。
        var layout = JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(CanvasLayout)))!;
        Assert.IsNotNull(layout["defaultJson_runtimeOnly"], "a type the archive carries can be written");
    }

    [TestMethod]
    public void ATypeOutsideTheTreeIsReportedAsSuch()
    {
        // 反向对照：目录是唯一事实源，没进目录的类型只答得出自己的名字。
        var schema = JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(string)))!;

        Assert.AreEqual(typeof(string).FullName, schema["fullName"]?.GetValue<string>());
        Assert.IsNull(schema["kind"]);
        Assert.IsNull(schema["properties"]);
    }

    [TestMethod]
    public void ResolveTypeIsClosedWorld()
    {
        Assert.AreEqual(typeof(TreeDefaultViewModel), TypeIntrospector.ResolveType(typeof(TreeDefaultViewModel).FullName!));
        Assert.IsNull(TypeIntrospector.ResolveType("System.String"), "the tree carries no entry for a BCL type");
    }

    /// <summary>A component declared with <c>[WorkflowBuilder.Node&lt;T&gt;]</c> and nothing else still has a schema.</summary>
    /// <remarks>
    /// The whole point of the declaration routes being recognised by the catalog is that the Agent can then ask
    /// about the type before constructing its JSON. A schema that came back as a bare name would answer the
    /// lookup and still leave the model guessing.
    /// </remarks>
    [TestMethod]
    public void AComponentDeclaredOnlyWithWorkflowBuilder_HasAClassSchema()
    {
        var schema = JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(UnannotatedNode)))!;

        Assert.AreEqual("class", schema["kind"]?.GetValue<string>());
        Assert.IsNotNull(schema["properties"], "a component the Agent may create has to describe what it carries");
        Assert.IsTrue(schema["jsonReadable"]!.GetValue<bool>(),
            "and the host has to be able to read it back, or CreateNode builds something that cannot be saved");
    }

    /// <summary>A <c>[VeloxProperty]</c> field is listed under the property name the Agent addresses it by.</summary>
    /// <remarks>
    /// Both spellings of the promotion rule are checked, because that name is what every writing tool takes —
    /// a schema listing the field name instead would send <c>PatchNodeProperties</c> after a member that does
    /// not exist.
    /// </remarks>
    [TestMethod]
    public void APromotedFieldIsListedUnderItsPromotedName()
    {
        var schema = JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(MemberShapeProbeNode)))!;
        var properties = schema["properties"]!.AsArray();

        var bare = properties.SingleOrDefault(p => p!["name"]!.GetValue<string>() == "Bare");
        Assert.IsNotNull(bare, "a field named 'bare' is listed as 'Bare': " + schema);
        Assert.AreEqual("string", bare!["type"]?.GetValue<string>());
        Assert.IsTrue(bare["canRead"]!.GetValue<bool>());
        Assert.IsTrue(bare["canWrite"]!.GetValue<bool>());

        var volume = properties.SingleOrDefault(p => p!["name"]!.GetValue<string>() == "Volume");
        Assert.IsNotNull(volume, "a field named '_volume' is listed as 'Volume': " + schema);
        Assert.AreEqual("int", volume!["type"]?.GetValue<string>());
    }

    /// <summary>The catalog and the archive are two different closed worlds, and the schema states both.</summary>
    /// <remarks>
    /// <c>jsonReadable</c> is the reason <c>GetTypeSchema</c> is told to come first: a type the catalog describes
    /// can still be one the archive has no reader for, and the model should learn that before it hands over a
    /// document rather than from a failure.
    /// </remarks>
    [TestMethod]
    public void TheCatalogAndTheArchiveAreDifferentClosedWorlds()
    {
        Assert.IsTrue(JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(CanvasLayout)))!["jsonReadable"]!.GetValue<bool>(),
            "a type the archive carries reads its own JSON back");

        Assert.IsFalse(JsonNode.Parse(TypeIntrospector.GetTypeSchema(typeof(CellKey)))!["jsonReadable"]!.GetValue<bool>(),
            "and one only the catalog knows about does not — CellKey is in the tree but in no archived document");
    }
}
