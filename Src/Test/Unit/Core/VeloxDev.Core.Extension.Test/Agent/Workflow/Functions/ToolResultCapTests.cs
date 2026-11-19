using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Pins the per-tool row caps. A production session can hold thousands of nodes, and the alternative to a
/// cap is the whole-result envelope, which replaces the answer instead of trimming it — the model then learns
/// nothing about the graph it asked for.
/// </summary>
[TestClass]
public class ToolResultCapTests
{
    private static WorkflowAgentScope TreeWith(int nodes)
    {
        var tree = new TreeDefaultViewModel();
        for (var i = 0; i < nodes; i++)
            tree.GetHelper().CreateNode(new NodeDefaultViewModel());
        return new WorkflowAgentScope(tree);
    }

    /// <summary>Omitting the cap keeps the historical shape: a bare array.</summary>
    [TestMethod]
    public void ListNodes_WithoutACap_StaysABareArray()
    {
        var json = WorkflowToolInvoker.Invoke(TreeWith(2), "ListNodes");

        Assert.AreEqual(JTokenType.Array, JToken.Parse(json).Type,
            "an uncapped ListNodes must not change the shape callers already parse");
    }

    [TestMethod]
    public void ListNodes_WithACap_ReportsWhatWasLeftOut()
    {
        var json = JObject.Parse(WorkflowToolInvoker.Invoke(TreeWith(3), "ListNodes", ("maxNodes", 2)));

        Assert.AreEqual(3, json["totalNodes"]!.Value<int>());
        Assert.AreEqual(2, json["returnedNodes"]!.Value<int>());
        Assert.IsTrue(json["truncated"]!.Value<bool>());
        Assert.HasCount(2, (json["nodes"] as JArray)!);
    }

    /// <summary>
    /// The cap trims the list but must not trim the count: a match beyond the cap is still a match, and a
    /// caller filtering on "how many are there" would otherwise get a number that changes with the page size.
    /// </summary>
    [TestMethod]
    public void FindNodes_WithACap_CountsEveryMatch()
    {
        var json = JObject.Parse(WorkflowToolInvoker.Invoke(
            TreeWith(3), "FindNodes", ("typeName", "Node"), ("maxMatches", 1)));

        Assert.AreEqual(3, json["matchCount"]!.Value<int>());
        Assert.AreEqual(1, json["returnedCount"]!.Value<int>());
        Assert.IsTrue(json["truncated"]!.Value<bool>());
    }

    [TestMethod]
    public void GetFullTopology_WithACap_SaysTheGraphIsIncomplete()
    {
        var json = JObject.Parse(WorkflowToolInvoker.Invoke(TreeWith(3), "GetFullTopology", ("maxNodes", 2)));

        Assert.AreEqual(3, json["totalNodes"]!.Value<int>());
        Assert.AreEqual(2, json["returnedNodes"]!.Value<int>());
        Assert.IsTrue(json["truncated"]!.Value<bool>());
    }
}
