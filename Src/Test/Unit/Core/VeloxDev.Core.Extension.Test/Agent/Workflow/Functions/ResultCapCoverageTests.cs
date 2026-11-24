using System;
using System.IO;
using System.Linq;
using Demo.Workflow;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The caps that are not per-tool: the global result cap, the execution log's tail, the schema cap and the
/// document cap. Each test also pins the size relationship the cap exists for — the capped answer is bounded
/// while the original length is still reported, so the model can narrow instead of losing the answer.
/// </summary>
[TestClass]
public class ResultCapCoverageTests
{
    private readonly System.Collections.Generic.List<string> _scratch = [];
    private readonly System.Collections.Generic.List<WorkflowDemoSession> _sessions = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var session in _sessions) session.Dispose();
        _sessions.Clear();
        foreach (var directory in _scratch)
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        _scratch.Clear();
    }

    private string Scratch()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-cap-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    private static WorkflowAgentScope TreeWith(int nodes)
    {
        var tree = new TreeDefaultViewModel();
        for (var i = 0; i < nodes; i++)
            tree.GetHelper().CreateNode(new NodeDefaultViewModel());
        return new WorkflowAgentScope(tree);
    }

    [TestMethod]
    public void GlobalCap_ReplacesAnOverlongResult_WithABoundedEnvelope()
    {
        var scope = TreeWith(60).WithMaxToolResultChars(200);

        var answer = WorkflowToolInvoker.Invoke(scope, "ListNodes");
        var json = JObject.Parse(answer);

        Assert.AreEqual("truncated", json["status"]?.Value<string>(), answer);
        Assert.AreEqual(200, json["limit"]?.Value<int>());
        Assert.IsTrue(json["originalLength"]?.Value<int>() > 200, "the original length is what tells the model how far to narrow");
        Assert.IsTrue(answer.Length < 600, $"the capped answer must stay near the cap, not near the original; got {answer.Length} chars");
    }

    [TestMethod]
    public void GlobalCap_LeavesASmallResultAlone()
    {
        var scope = TreeWith(1).WithMaxToolResultChars(10_000);

        var json = JToken.Parse(WorkflowToolInvoker.Invoke(scope, "ListNodes"));

        Assert.AreEqual(JTokenType.Array, json.Type, "a result under the cap keeps its historical shape");
    }

    [TestMethod]
    public void SerializeWorkflow_OverTheGlobalCap_IsReplacedByTheEnvelope()
    {
        var scope = TreeWith(40).WithMaxToolResultChars(400);

        var answer = WorkflowToolInvoker.Invoke(scope, "SerializeWorkflow");
        var json = JObject.Parse(answer);

        Assert.AreEqual("truncated", json["status"]?.Value<string>(), answer);
        Assert.AreEqual(400, json["limit"]?.Value<int>());
        Assert.IsTrue(json["originalLength"]?.Value<int>() > 400);
    }

    [TestMethod]
    public void GetExecutionLog_KeepsTheMostRecentEntries()
    {
        var session = WorkflowDemoSession.Create(Scratch());
        _sessions.Add(session);
        for (var i = 1; i <= 5; i++)
            session.Tree.ExecutionLog.Add($"entry {i}");

        var capped = JObject.Parse(WorkflowToolInvoker.Invoke(
            session.Tree.AsAgentScope(), "GetExecutionLog", ("maxEntries", 2)));

        Assert.AreEqual(5, capped["entryCount"]?.Value<int>());
        Assert.AreEqual(2, capped["returnedCount"]?.Value<int>());
        Assert.AreEqual(true, capped["truncated"]?.Value<bool>());
        CollectionAssert.AreEqual(new[] { "entry 4", "entry 5" },
            (capped["entries"] as JArray)!.Select(v => v.Value<string>()).ToArray(),
            "the tail is the useful half of a log");

        var all = JObject.Parse(WorkflowToolInvoker.Invoke(session.Tree.AsAgentScope(), "GetExecutionLog"));
        Assert.AreEqual(5, all["returnedCount"]?.Value<int>());
        Assert.AreEqual(false, all["truncated"]?.Value<bool>());
    }

    [TestMethod]
    public void GetTypeSchema_OverTheCap_ReturnsThePreviewAndOriginalLength()
    {
        var scope = TreeWith(1);

        var answer = WorkflowToolInvoker.Invoke(scope, "GetTypeSchema",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("maxChars", 80));
        var json = JObject.Parse(answer);
        var preview = json["preview"]?.Value<string>() ?? string.Empty;

        Assert.AreEqual("truncated", json["status"]?.Value<string>(), answer);
        Assert.AreEqual(80, preview.Length);
        Assert.IsTrue(json["originalLength"]?.Value<int>() > 80);
    }
}
