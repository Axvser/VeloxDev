using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Demo.ViewModels;
using Demo.Workflow;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.Core.Extension.Test.Examples;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The node-level execution entry points must return when their caller asks them to, even when the node's
/// business code never finishes. Before <c>timeoutMs</c> existed the tool waited on the command indefinitely:
/// one hung script held the Agent's turn open with no answer to give the user.
/// <para>
/// The assertions are on wall-clock bounds as well as on the envelope: the point of a timeout is that the
/// call comes back, not only that it says the right word.
/// </para>
/// </summary>
[TestClass]
public class ExecutionTimeoutTests
{
    private readonly List<string> _scratch = [];
    private readonly List<WorkflowDemoSession> _sessions = [];

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
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-timeout-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    /// <summary>A demo session whose interpreter takes long enough that only the timeout can end the wait.</summary>
    private WorkflowDemoSession Hanging(TimeSpan delay)
    {
        var session = WorkflowDemoSession.Create(Scratch());
        _sessions.Add(session);
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
            node.SetHelper(new StubPythonHelper { PerCallDelay = delay });
        return session;
    }

    private static int PythonIndex(WorkflowDemoSession session)
        => session.Tree.Nodes.ToList().FindIndex(n => n is PythonScriptNodeViewModel);

    private static int ControllerIndex(WorkflowDemoSession session)
        => session.Tree.Nodes.ToList().FindIndex(n => n is ControllerViewModel);

    [TestMethod]
    public void ExecuteNode_ReportsATimeout_InsteadOfHanging()
    {
        var session = Hanging(TimeSpan.FromSeconds(5));
        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);
        var index = PythonIndex(session);

        var clock = Stopwatch.StartNew();
        var answer = WorkflowToolInvoker.Invoke(scope, "ExecuteNode", ("nodeIndex", index), ("timeoutMs", 100));
        clock.Stop();

        var json = JObject.Parse(answer);
        Assert.AreEqual("error", json["status"]?.Value<string>(), answer);
        StringAssert.Contains(json["message"]?.Value<string>() ?? string.Empty, "did not finish within 100 ms");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(2),
            $"the tool must return at the timeout, not when the node finally ends; took {clock.Elapsed}");
    }

    [TestMethod]
    public void ExecuteNodes_ReportsPerNodeTimeouts_AndKeepsGoing()
    {
        var session = Hanging(TimeSpan.FromSeconds(5));
        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);
        var indices = session.Tree.Nodes
            .Select((node, index) => (node, index))
            .Where(pair => pair.node is PythonScriptNodeViewModel)
            .Take(2)
            .Select(pair => pair.index)
            .ToArray();
        Assert.HasCount(2, indices, "the fixture needs two Python nodes");

        var clock = Stopwatch.StartNew();
        var answer = WorkflowToolInvoker.Invoke(scope, "ExecuteNodes",
            ("nodeIndicesJson", $"[{string.Join(',', indices)}]"), ("timeoutMs", 100));
        clock.Stop();

        var json = JObject.Parse(answer);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), answer);
        Assert.AreEqual(0, json["completed"]?.Value<int>(), "neither node finished");
        Assert.HasCount(2, (json["errors"] as JArray)!, "each timed-out node gets its own line");
        StringAssert.Contains(answer, "did not finish within 100 ms");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(2),
            $"a per-node timeout must bound the whole batch; took {clock.Elapsed}");
    }

    [TestMethod]
    public void RunCompiledWorkflow_TimesOut_WithAnError()
    {
        var session = Hanging(TimeSpan.FromSeconds(5));
        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);
        var start = ControllerIndex(session);

        var clock = Stopwatch.StartNew();
        var answer = WorkflowToolInvoker.Invoke(scope, "RunCompiledWorkflow",
            ("startNodeIndex", start), ("timeoutMs", 100));
        clock.Stop();

        var json = JObject.Parse(answer);
        Assert.AreEqual("error", json["status"]?.Value<string>(), answer);
        StringAssert.Contains(json["message"]?.Value<string>() ?? string.Empty, "timed out after 100 ms");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(2),
            $"the waiting entry must return at its timeout; took {clock.Elapsed}");
    }
}
