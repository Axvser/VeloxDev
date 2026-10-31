using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.Core.Extension.Test.Examples;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The handle registry and the payload cap that were added so a production session can hold a run and read its
/// result without either leaking the handle or pasting an arbitrarily large payload into the conversation.
/// <para>
/// <c>ListCompiledRuns</c> is deliberately used to wait for the end: unlike <c>GetCompiledRunStatus</c> it does
/// not retire a finished handle, so these tests observe the registry itself rather than the retirement path.
/// </para>
/// </summary>
[TestClass]
public class CompiledRunHandleCoverageTests
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
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-runs-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    private WorkflowDemoSession Session(TimeSpan? perCall = null)
    {
        var session = WorkflowDemoSession.Create(Scratch());
        _sessions.Add(session);
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
            node.SetHelper(new StubPythonHelper { PerCallDelay = perCall ?? TimeSpan.Zero });
        return session;
    }

    private static int ControllerIndex(WorkflowDemoSession session)
        => session.Tree.Nodes.ToList().FindIndex(n => n is ControllerViewModel);

    private static string Start(WorkflowAgentScope scope, int startNodeIndex)
    {
        var started = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "StartCompiledWorkflow",
            ("startNodeIndex", startNodeIndex)));
        Assert.AreEqual("ok", started["status"]?.Value<string>(), started.ToString());
        return started["handle"]!.Value<string>()!;
    }

    private static JObject Runs(WorkflowAgentScope scope)
        => JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ListCompiledRuns"));

    /// <summary>Waits through <c>ListCompiledRuns</c> so a finished handle is still registered when it returns.</summary>
    private static async Task<JObject> WaitForFinishedHandleAsync(WorkflowAgentScope scope, string handle, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        JObject last = Runs(scope);
        while (DateTime.UtcNow < deadline)
        {
            last = Runs(scope);
            var row = (last["runs"] as JArray)!
                .FirstOrDefault(r => string.Equals(r["handle"]?.Value<string>(), handle, StringComparison.OrdinalIgnoreCase));
            if (row?["isRunning"]?.Value<bool>() == false) return last;
            await Task.Delay(25);
        }

        Assert.Fail($"handle '{handle}' did not finish within {seconds}s; last list: {last}");
        return last;
    }

    [TestMethod]
    public async Task ListCompiledRuns_SeesAStartedRun_AndClearFinishedRuns_RetiresIt()
    {
        var session = Session();
        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);
        var handle = Start(scope, ControllerIndex(session));

        var finished = await WaitForFinishedHandleAsync(scope, handle);
        Assert.AreEqual(1, finished["count"]?.Value<int>());
        var row = (finished["runs"] as JArray)![0];
        Assert.AreEqual(handle, row["handle"]?.Value<string>());
        Assert.AreEqual(false, row["isRunning"]?.Value<bool>());

        var cleared = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ClearFinishedRuns"));
        Assert.AreEqual("ok", cleared["status"]?.Value<string>(), cleared.ToString());
        StringAssert.Contains(cleared["message"]?.Value<string>() ?? string.Empty, "Cleared 1");

        Assert.AreEqual(0, Runs(scope)["count"]?.Value<int>(), "a retired handle must be gone from the next list");
    }

    [TestMethod]
    public async Task ClearFinishedRuns_LeavesARunningHandleAlone()
    {
        var session = Session(TimeSpan.FromSeconds(2));
        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);
        var handle = Start(scope, ControllerIndex(session));

        var running = Runs(scope);
        var row = (running["runs"] as JArray)!.FirstOrDefault(r =>
            string.Equals(r["handle"]?.Value<string>(), handle, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(row, "the freshly started run must be listed");
        Assert.AreEqual(true, row!["isRunning"]?.Value<bool>());

        var cleared = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ClearFinishedRuns"));
        StringAssert.Contains(cleared["message"]?.Value<string>() ?? string.Empty, "Cleared 0");
        Assert.AreEqual(1, Runs(scope)["count"]?.Value<int>(), "a running handle is not retired");

        var stopped = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "StopCompiledRun", ("handle", handle)));
        Assert.AreEqual("ok", stopped["status"]?.Value<string>(), stopped.ToString());
        await Task.Delay(100);
    }

    /// <summary>Stand-in for the ticker: it puts a payload of the host's choosing into the run's data.</summary>
    private sealed class FixedTimerHelper(object payload) : TimerHelper
    {
        public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
            => Task.FromResult<object?>(payload);
    }

    [TestMethod]
    public async Task GetCompiledRunStatus_CapsTheFinalPayload()
    {
        var session = Session();
        var payload = new string('x', 400);
        session.Tree.Nodes.OfType<TimerNodeViewModel>().Single().SetHelper(new FixedTimerHelper(payload));
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
            node.SetHelper(new StubPythonHelper { CancelAtInvocation = 1 });

        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);
        var handle = Start(scope, ControllerIndex(session));
        await WaitForFinishedHandleAsync(scope, handle);

        var status = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "GetCompiledRunStatus",
            ("handle", handle), ("maxDataChars", 64)));
        Assert.AreEqual("ok", status["status"]?.Value<string>(), status.ToString());

        var data = status["data"] as JObject;
        Assert.IsNotNull(data, "an over-long payload is replaced by an object envelope: " + status);
        Assert.AreEqual("truncated", data!["status"]?.Value<string>());
        Assert.AreEqual(64, data["limit"]?.Value<int>());
        Assert.IsTrue(data["originalLength"]?.Value<int>() > 64);
        Assert.AreEqual(64, (data["preview"]?.Value<string>() ?? string.Empty).Length);
    }
}
