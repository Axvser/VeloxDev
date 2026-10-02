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

        // 值类型恒能无参构造，所以默认实例那条路对它同样成立。
        Assert.IsNotNull(schema["defaultJson_runtimeOnly"], "a struct has a default instance to serialize");
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
}
