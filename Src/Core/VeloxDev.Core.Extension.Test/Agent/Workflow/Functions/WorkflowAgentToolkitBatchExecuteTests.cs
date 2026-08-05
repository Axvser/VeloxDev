using Demo.ViewModels;
using Demo.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Threading;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Regression tests for the <see cref="WorkflowAgentToolkit"/> BatchExecute dispatch map.
/// Pins the <c>SetEnumSlotCollection</c> argument pass-through: the non-enum ISlotProvider
/// path (<c>nonEnumTypeName</c>) and the enum path, which previously mis-named the third key
/// (<c>fullEnumTypeName</c> instead of <c>selectorTypeOrJson</c>) and never forwarded the
/// fourth argument, making non-enum selectors silently fall back to the enum path.
/// </summary>
[TestClass]
public class WorkflowAgentToolkitBatchExecuteTests
{
    [TestMethod]
    public void BatchExecute_SetEnumSlotCollection_NonEnumPath_ReceivesNonEnumTypeName()
    {
        var session = WorkflowDemoSession.Create();
        var tree = session.Tree;
        var node = tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        int idx = tree.Nodes.IndexOf(node);
        Assert.IsGreaterThan(-1, idx);

        var selectorJson = new JObject(
            new JProperty("Routes", new JArray(
                new JObject(new JProperty("Key", "A"), new JProperty("Label", "Route A")),
                new JObject(new JProperty("Key", "B"), new JProperty("Label", "Route B"))
            ))
        ).ToString(Formatting.None);

        var ops = new JArray(
            new JObject(
                new JProperty("tool", "SetEnumSlotCollection"),
                new JProperty("args", new JObject(
                    new JProperty("nodeIndex", idx),
                    new JProperty("propertyName", "OutputSlots"),
                    new JProperty("selectorTypeOrJson", selectorJson),
                    new JProperty("nonEnumTypeName", "Demo.ViewModels.CustomRouteSelector")
                ))
            )
        ).ToString(Formatting.None);

        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));
        var result = InvokeBatchAsync(toolkit, ops);

        var results = JArray.Parse(result);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("SetEnumSlotCollection", results[0]["tool"]?.ToString());

        var inner = JObject.Parse(results[0]["result"]?.ToString()!);
        Assert.IsTrue(inner["ok"]!.Value<bool>(),
            "non-enum path must succeed when nonEnumTypeName is forwarded through the batch dispatch");
        Assert.AreEqual("Demo.ViewModels.CustomRouteSelector", inner["selectorType"]?.ToString());
    }

    [TestMethod]
    public void BatchExecute_SetEnumSlotCollection_EnumPath_StillWorks()
    {
        var session = WorkflowDemoSession.Create();
        var tree = session.Tree;
        var node = tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        int idx = tree.Nodes.IndexOf(node);
        Assert.IsGreaterThan(-1, idx);

        var ops = new JArray(
            new JObject(
                new JProperty("tool", "SetEnumSlotCollection"),
                new JProperty("args", new JObject(
                    new JProperty("nodeIndex", idx),
                    new JProperty("propertyName", "OutputSlots"),
                    new JProperty("selectorTypeOrJson", "Demo.ViewModels.VoltageRange")
                ))
            )
        ).ToString(Formatting.None);

        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));
        var result = InvokeBatchAsync(toolkit, ops);

        var results = JArray.Parse(result);
        Assert.AreEqual(1, results.Count);

        var inner = JObject.Parse(results[0]["result"]?.ToString()!);
        Assert.IsTrue(inner["ok"]!.Value<bool>());
        Assert.AreEqual("Demo.ViewModels.VoltageRange", inner["selectorType"]?.ToString());
    }

    [TestMethod]
    public void GetFullTopology_IncludesLinkIds()
    {
        var session = WorkflowDemoSession.Create();
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(session.Tree));

        var method = typeof(WorkflowAgentToolkit)
            .GetMethod("GetFullTopology", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "GetFullTopology method was not found.");

        var raw = method.Invoke(toolkit, null);
        Assert.IsInstanceOfType(raw, typeof(string));
        var topology = JObject.Parse((string)raw!);

        var links = topology["links"] as JArray;
        Assert.IsNotNull(links);
        Assert.IsGreaterThan(0, links!.Count, "demo session should have visible connections");
        foreach (var link in links)
        {
            Assert.IsNotNull(link["id"], "every GetFullTopology link entry must carry a runtime id");
            Assert.IsNotNull(link["sid"], "every link entry must carry a sender slot id");
            Assert.IsNotNull(link["rid"], "every link entry must carry a receiver slot id");
        }
    }

    private static string InvokeBatchAsync(WorkflowAgentToolkit toolkit, string operationsJson)
    {
        var method = typeof(WorkflowAgentToolkit)
            .GetMethod("BatchExecute", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "BatchExecute method was not found.");

        var task = (Task<string>)method.Invoke(toolkit, [operationsJson, CancellationToken.None])!;
        return task.GetAwaiter().GetResult();
    }
}
