using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Demo.ViewModels;
using Demo.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.Core.Extension.Test.Examples;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The Agent's hold on a compiled run: it starts one, gets a handle, and can then hold it, let it go, stop it and
/// carry on from where it stopped — none of which the waiting entry (<c>RunCompiledWorkflow</c>) can do, because it
/// only returns once the run is over.
/// <para>
/// Driven through the public tool surface, on the demo's own graph with the interpreter stubbed, so what is under
/// test is the tool contract rather than a body called directly.
/// </para>
/// </summary>
[TestClass]
public class CompiledRunControlTests
{
    private readonly List<string> _scratch = [];

    private readonly List<WorkflowDemoSession> _sessions = [];

    private TextWriterLogWriter? _logWriter;

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var session in _sessions) session.Dispose();
        _sessions.Clear();
        _logWriter?.Dispose();   // 谁开的文件谁关：这个句柄是测试开的，scope 只是借用
        _logWriter = null;
        foreach (var directory in _scratch)
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        _scratch.Clear();
    }

    private string Scratch()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-agent-run-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    /// <summary>The demo tree with a stubbed interpreter, and the scope the Agent's tools are reached through.</summary>
    private (WorkflowAgentScope Scope, WorkflowDemoSession Session) Build(TimeSpan? perCall = null)
    {
        var session = WorkflowDemoSession.Create(Scratch());
        _sessions.Add(session);
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
            node.SetHelper(new StubPythonHelper { PerCallDelay = perCall ?? TimeSpan.Zero });

        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);
        return (scope, session);
    }

    private static int ControllerIndex(WorkflowDemoSession session)
        => session.Tree.Nodes.ToList().FindIndex(n => n is ControllerViewModel);

    private static string Start(WorkflowAgentScope scope, int startNodeIndex, string toolName = "StartCompiledWorkflow")
    {
        var started = JObject.Parse(WorkflowToolInvoker.Invoke(scope, toolName, ("startNodeIndex", startNodeIndex)));
        Assert.AreEqual("ok", started["status"]?.Value<string>(), started.ToString());
        return started["handle"]!.Value<string>()!;
    }

    private static JObject Status(WorkflowAgentScope scope, string handle)
        => JObject.Parse(WorkflowToolInvoker.Invoke(scope, "GetCompiledRunStatus", ("handle", handle)));

    /// <summary>
    /// Polls until the run is no longer running. The call that sees the end also retires the handle, so the status
    /// it returns is the last word on that run.
    /// </summary>
    private static async Task<JObject> WaitForEndAsync(WorkflowAgentScope scope, string handle, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var status = Status(scope, handle);
            if (status["isRunning"]?.Value<bool>() == false) return status;
            await Task.Delay(25);
        }
        var stuck = Status(scope, handle);
        Assert.Fail($"run '{handle}' did not finish in {seconds}s; last status: {stuck.ToString(Formatting.None)}");
        return null!;
    }

    [TestMethod]
    public async Task AStartedRun_CanBeHeld_LetGo_AndFollowedToItsEnd()
    {
        // Each node takes a moment, so the run is still in flight when the hold arrives.
        var (scope, session) = Build(TimeSpan.FromMilliseconds(40));
        var handle = Start(scope, ControllerIndex(session));

        var held = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "PauseCompiledRun", ("handle", handle)));
        Assert.AreEqual("ok", held["status"]?.Value<string>());

        // The hold lands at a node boundary, so the run is still going — parked, not finished.
        var parked = Status(scope, handle);
        Assert.IsTrue(parked["isPaused"]!.Value<bool>(), $"the run must report itself held: {parked}");
        Assert.IsTrue(parked["isRunning"]!.Value<bool>(), "held is not finished");

        var released = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ResumeCompiledRun", ("handle", handle)));
        Assert.AreEqual("ok", released["status"]?.Value<string>());

        var ended = await WaitForEndAsync(scope, handle);
        Assert.AreEqual("Completed", ended["outcome"]?.Value<string>(), ended.ToString());
        Assert.IsFalse(ended["isPaused"]!.Value<bool>());
        Assert.IsTrue(ended["attempts"]!.Value<int>() >= 1);
    }

    [TestMethod]
    public async Task AStoppedRun_EndsAsCancelled_AndTheAgentCanCarryOnFromIt()
    {
        var (scope, session) = Build(TimeSpan.FromMilliseconds(40));
        var handle = Start(scope, ControllerIndex(session));

        var stopped = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "StopCompiledRun", ("handle", handle)));
        Assert.AreEqual("ok", stopped["status"]?.Value<string>());

        var cancelled = await WaitForEndAsync(scope, handle);
        Assert.AreEqual("Cancelled", cancelled["outcome"]?.Value<string>(),
            "a run the Agent stopped is a cancellation, not a failure");

        // The place it left is in the scope's own store, so continuing needs no host configuration.
        var continuedHandle = Start(scope, ControllerIndex(session), "ContinueCompiledWorkflow");
        var continued = await WaitForEndAsync(scope, continuedHandle);

        Assert.AreEqual("Completed", continued["outcome"]?.Value<string>(), continued.ToString());
        Assert.IsTrue(continued["logCount"]!.Value<int>() > 0, "the resumed run drove nodes of its own");
    }

    [TestMethod]
    public async Task CarryingOn_WithNothingWrittenYet_IsRefused()
    {
        var (scope, session) = Build();

        var refused = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ContinueCompiledWorkflow",
            ("startNodeIndex", ControllerIndex(session))));

        Assert.AreEqual("error", refused["status"]?.Value<string>());
        StringAssert.Contains(refused["message"]!.Value<string>()!, "no checkpoint",
            "the answer must say what is missing rather than start a fresh run behind the Agent's back");
    }

    [TestMethod]
    public async Task TheFailureRecords_AndTheLogFile_TravelWithTheResult()
    {
        var logPath = Path.Combine(Scratch(), "workflow.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var (scope, session) = Build();
        _logWriter = TextWriterLogWriter.For(logPath);

        // Two host settings in one line each: where the lines go, and the policy that makes the demo's publish
        // step try again after its first delivery fails. Neither is the Agent's business to guess — they are the
        // host's, and this is the hook that gets them into a run the Agent starts.
        scope.WithLogWriter(_logWriter)
             .WithSessionConfiguration(context => context.RetryPolicy = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 10));

        var result = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "RunCompiledWorkflow",
            ("startNodeIndex", ControllerIndex(session))));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.AreEqual("Completed", result["outcome"]?.Value<string>(), "the demo graph finishes, redirect and retry included");

        var failures = (JArray)result["failures"]!;
        Assert.IsTrue(failures.Count > 0, "the run's warnings and its error travel as records");
        Assert.IsTrue(failures.Any(f => f["level"]?.Value<string>() == "Warning"),
            $"the audit's warning about the flat set is one of them: {failures}");
        Assert.IsTrue(failures.Any(f => f["level"]?.Value<string>() == "Error"),
            $"and so is the audit's error: {failures}");

        // The path is the whole point: with it the model can open the file with a tool of its own.
        var logFile = result["logFile"]!.Value<string>();
        Assert.AreEqual(Path.GetFullPath(logPath), logFile, "an absolute path, not a relative one");

        // 谁开的文件谁关：这个句柄是测试开的，运行结束就该放开，然后才读得到。
        _logWriter?.Dispose();
        _logWriter = null;

        Assert.IsTrue(File.Exists(logFile), "and the lines really went there");
        StringAssert.Contains(File.ReadAllText(logFile), "[Retry 1]",
            "the run retried the publish step — because the host's policy said so, through WithSessionConfiguration");
    }
}
