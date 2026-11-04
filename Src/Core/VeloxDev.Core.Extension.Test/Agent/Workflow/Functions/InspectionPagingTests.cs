using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The read-side tools must stay usable on a graph that does not fit in one tool result. These tests cover
/// the paging/filtering contracts added to <c>ListConnections</c>, the summarised <c>SerializeWorkflow</c>
/// modes, the layout summary, and the domain-validator hook on <c>ValidateWorkflow</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class InspectionPagingTests
{
    [TestCleanup]
    public void ClearValidators() => WorkflowValidation.Clear();

    /// <summary>Three nodes in a chain (0 → 1 → 2) with two visible links.</summary>
    private static (TreeDefaultViewModel Tree, WorkflowAgentScope Scope) Chain()
    {
        var tree = new TreeDefaultViewModel();
        var nodes = new[]
        {
            new NodeDefaultViewModel { Parent = tree },
            new NodeDefaultViewModel { Parent = tree },
            new NodeDefaultViewModel { Parent = tree },
        };
        foreach (var node in nodes)
        {
            tree.Nodes.Add(node);
            node.SetAnchorCommand.Execute(new Anchor(5, 5, 0));
            node.SetSizeCommand.Execute(new Size(80, 40));
        }

        nodes[0].CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneTarget });
        nodes[1].CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneSource });
        nodes[1].CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneTarget });
        nodes[2].CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneSource });

        tree.GetHelper().SendConnection(nodes[0].Slots[0]);
        tree.GetHelper().ReceiveConnection(nodes[1].Slots[0]);
        tree.GetHelper().SendConnection(nodes[1].Slots[1]);
        tree.GetHelper().ReceiveConnection(nodes[2].Slots[0]);
        foreach (var link in tree.Links) link.IsVisible = true;

        return (tree, new WorkflowAgentScope(tree));
    }

    [TestMethod]
    public void ListConnectionsWithoutArgumentsKeepsTheBareArray()
    {
        var (_, scope) = Chain();

        var answer = JArray.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections"));

        Assert.AreEqual(2, answer.Count);
        Assert.IsNotNull(answer[0]["id"]);
    }

    [TestMethod]
    public void ListConnectionsFiltersByNodeAndDirection()
    {
        var (_, scope) = Chain();

        var outgoing = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections",
            ("nodeIndex", 0), ("direction", "out")));
        Assert.AreEqual("ok", outgoing["status"]?.Value<string>(), outgoing.ToString());
        Assert.AreEqual(1, outgoing["total"]!.Value<int>());
        Assert.AreEqual(1, outgoing["returned"]!.Value<int>());
        Assert.AreEqual(false, outgoing["truncated"]!.Value<bool>());

        var incoming = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections",
            ("nodeIndex", 0), ("direction", "in")));
        Assert.AreEqual(0, incoming["total"]!.Value<int>());

        var both = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections",
            ("nodeIndex", 1), ("direction", "any")));
        Assert.AreEqual(2, both["total"]!.Value<int>(), "node 1 receives and sends one link");
    }

    [TestMethod]
    public void ListConnectionsPagesWithTotalAndNextOffset()
    {
        var (_, scope) = Chain();

        var first = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections",
            ("maxLinks", 1)));
        Assert.AreEqual(2, first["total"]!.Value<int>());
        Assert.AreEqual(1, first["returned"]!.Value<int>());
        Assert.AreEqual(true, first["truncated"]!.Value<bool>());
        Assert.AreEqual(1, first["nextOffset"]!.Value<int>());
        Assert.AreEqual(1, first["links"]!.Children().Count());

        var second = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections",
            ("offset", 1), ("maxLinks", 1)));
        Assert.AreEqual(1, second["returned"]!.Value<int>());
        Assert.AreEqual(false, second["truncated"]!.Value<bool>());
        Assert.IsNull(second["nextOffset"], "the last page has no next offset");
    }

    [TestMethod]
    public void ListConnectionsRejectsAnUnknownDirection()
    {
        var (_, scope) = Chain();

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections",
            ("nodeIndex", 0), ("direction", "sideways")));

        Assert.AreEqual("error", answer["status"]?.Value<string>());
    }

    [TestMethod]
    public void SerializeWorkflowSummaryReportsCountsWithoutTheDocument()
    {
        var (_, scope) = Chain();

        var summary = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "SerializeWorkflow",
            ("mode", "summary")));

        Assert.AreEqual("ok", summary["status"]?.Value<string>(), summary.ToString());
        Assert.AreEqual("summary", summary["mode"]?.Value<string>());
        Assert.AreEqual(3, summary["nodeCount"]!.Value<int>());
        Assert.AreEqual(2, summary["linkCount"]!.Value<int>());
        Assert.AreEqual(3, summary["nodeTypes"]!["NodeDefaultViewModel"]!.Value<int>());
        Assert.IsNull(summary["nodes"], "summary does not carry the document");
    }

    [TestMethod]
    public void SerializeWorkflowSkeletonReportsNodesAndLinksOnly()
    {
        var (_, scope) = Chain();

        var skeleton = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "SerializeWorkflow",
            ("mode", "skeleton")));

        Assert.AreEqual("skeleton", skeleton["mode"]?.Value<string>());
        Assert.AreEqual(3, skeleton["nodes"]!.Children().Count());
        Assert.AreEqual(2, skeleton["links"]!.Children().Count());
        Assert.IsNotNull(skeleton["nodes"]![0]!["t"]);
        Assert.IsNull(skeleton["nodes"]![0]!["Title"], "skeleton omits custom properties");
    }

    [TestMethod]
    public void SerializeWorkflowSkeletonHonoursMaxChars()
    {
        var (_, scope) = Chain();

        var capped = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "SerializeWorkflow",
            ("mode", "skeleton"), ("maxChars", 80)));

        Assert.AreEqual("truncated", capped["status"]?.Value<string>(), capped.ToString());
        Assert.IsTrue(capped["originalLength"]!.Value<int>() > 80);
        Assert.AreEqual(80, capped["limit"]!.Value<int>());
    }

    [TestMethod]
    public void PreviewLayoutSummaryOmitsPlacements()
    {
        var (_, scope) = Chain();

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "PreviewLayout",
            ("summaryOnly", true)));

        Assert.AreEqual("ok", answer["status"]?.Value<string>(), answer.ToString());
        Assert.IsNull(answer["placements"], "summaryOnly omits the placement array");
        Assert.IsNotNull(answer["changedCount"]);
        Assert.IsNotNull(answer["cost"]);
    }

    [TestMethod]
    public void PreviewLayoutChangedOnlyCapsPlacements()
    {
        var (_, scope) = Chain();

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "PreviewLayout",
            ("changedOnly", true), ("maxPlacements", 1)));

        Assert.AreEqual("ok", answer["status"]?.Value<string>(), answer.ToString());
        var placements = answer["placements"]!.Children().ToList();
        Assert.IsTrue(placements.Count <= 1);
        Assert.IsTrue(placements.All(placement => placement["moved"]!.Value<bool>()));
        Assert.IsTrue(answer["totalPlacements"]!.Value<int>() >= placements.Count);
    }

    private sealed class StubValidator : IWorkflowTreeValidator
    {
        public string Name => "stub-domain";

        public IEnumerable<WorkflowValidationIssue> Validate(IWorkflowTreeViewModel tree)
        {
            yield return new WorkflowValidationIssue(
                "error", 0, "component-0", "Missing required field", "TEST001");
        }
    }

    private sealed class ThrowingValidator : IWorkflowTreeValidator
    {
        public string Name => "throwing-domain";

        public IEnumerable<WorkflowValidationIssue> Validate(IWorkflowTreeViewModel tree)
            => throw new InvalidOperationException("validator exploded");
    }

    [TestMethod]
    public void ValidateWorkflowIncludesRegisteredDomainValidators()
    {
        var (_, scope) = Chain();
        WorkflowValidation.Register(new StubValidator());
        Assert.AreEqual(1, WorkflowValidation.Snapshot().Count, "the validator is registered");

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ValidateWorkflow"));
        var records = answer["warnings"]!.Children().ToList();
        Assert.IsTrue(
            records.Any(record => record["validator"]?.Value<string>() == "stub-domain"),
            answer.ToString());

        var issue = records.Single(record => record["validator"]?.Value<string>() == "stub-domain");
        Assert.AreEqual("error", issue["level"]?.Value<string>());
        Assert.AreEqual("TEST001", issue["code"]?.Value<string>());
        Assert.AreEqual("Missing required field", issue["msg"]?.Value<string>());
        Assert.AreEqual("component-0", issue["id"]?.Value<string>());
    }

    [TestMethod]
    public void ValidateWorkflowReportsAThrowingValidatorWithoutFailingTheCall()
    {
        var (_, scope) = Chain();
        WorkflowValidation.Register(new ThrowingValidator());

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ValidateWorkflow"));
        var records = answer["warnings"]!.Children().ToList();
        Assert.IsTrue(
            records.Any(record => record["validator"]?.Value<string>() == "throwing-domain"),
            answer.ToString());

        var issue = records.Single(record => record["validator"]?.Value<string>() == "throwing-domain");
        Assert.AreEqual("warn", issue["level"]?.Value<string>());
        StringAssert.Contains(issue["msg"]!.Value<string>()!, "validator exploded");
    }
}
