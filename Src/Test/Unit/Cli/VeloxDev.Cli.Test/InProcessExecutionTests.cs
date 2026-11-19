using System.Text.Json;
using Microsoft.Extensions.AI;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Cli.Test.Plugin;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Cli.Test;

/// <summary>
/// The in-process execution surface the CLI exposes on top of the toolkit: a handle can be held, released, stopped
/// and carried on from its checkpoint. The library has its own tests for these tools; this one proves the CLI's
/// launch flags and tool wiring reach the same code with a real archive and a real component assembly.
/// </summary>
[TestClass]
public class InProcessExecutionTests
{
    private static CliOptions Options(string archive, params string[] extra)
        => CliOptions.Parse(["mcp", "--archive", archive, .. extra]);

    private static async Task<JsonDocument> InvokeAsync(
        WorkflowSession session, string name, params (string Name, object? Value)[] args)
    {
        var function = session.Toolkit.CreateTools(WorkflowToolCategory.All)
            .OfType<AIFunction>()
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Tool '{name}' is not registered.");

        var arguments = new AIFunctionArguments();
        foreach (var (key, value) in args)
            arguments[key] = value;

        var result = await function.InvokeAsync(arguments);
        return JsonDocument.Parse(result?.ToString() ?? "null");
    }

    private static string WriteChain(TestArchive fixture, int count, bool flakyTail = false)
    {
        var tree = new TreeDefaultViewModel();
        var nodes = new List<IWorkflowNodeViewModel>();

        for (var index = 0; index < count; index++)
        {
            IWorkflowNodeViewModel node = flakyTail && index == count - 1
                ? new FlakyNodeViewModel()
                : new SlowNodeViewModel();
            tree.GetHelper().CreateNode(node);
            nodes.Add(node);
        }

        for (var index = 0; index < nodes.Count - 1; index++)
        {
            var output = ((SlowNodeViewModel)nodes[index]).OutputSlot;
            var input = nodes[index + 1] is FlakyNodeViewModel flaky ? flaky.InputSlot : ((SlowNodeViewModel)nodes[index + 1]).InputSlot;
            output.Channel = SlotChannel.MultipleTargets;
            input.Channel = SlotChannel.MultipleSources;
            tree.SendConnectionCommand.Execute(output);
            tree.ReceiveConnectionCommand.Execute(input);
        }

        return fixture.WriteTree(tree);
    }

    private static async Task<JsonDocument> WaitForEndAsync(WorkflowSession session, string handle, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        JsonDocument? last = null;

        while (DateTime.UtcNow < deadline)
        {
            var status = await InvokeAsync(session, "GetCompiledRunStatus", ("handle", handle));
            if (status.RootElement.GetProperty("status").GetString() == "ok")
            {
                last?.Dispose();
                last = status;
                if (!status.RootElement.GetProperty("isRunning").GetBoolean())
                    return status;
            }
            else
            {
                status.Dispose();
            }

            await Task.Delay(25);
        }

        Assert.Fail($"The run did not finish in time: {last?.RootElement.ToString() ?? "no status"}");
        return last!;
    }

    private static WorkflowSession CreateSession(string archive)
        => WorkflowSession.Create(Options(
            archive, "--allow-execution", "--in-process-execution",
            "--assembly", typeof(SlowNodeViewModel).Assembly.Location));

    [TestMethod]
    public async Task APausedRun_CanBeResumed_AndCompletes()
    {
        using var fixture = new TestArchive();
        var archive = WriteChain(fixture, count: 8);
        await using var session = CreateSession(archive);

        using var started = await InvokeAsync(session, "StartCompiledWorkflow", ("startNodeIndex", 0));
        var handle = started.RootElement.GetProperty("handle").GetString()!;

        using var paused = await InvokeAsync(session, "PauseCompiledRun", ("handle", handle));
        Assert.AreEqual("ok", paused.RootElement.GetProperty("status").GetString());

        JsonDocument? parked = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var status = await InvokeAsync(session, "GetCompiledRunStatus", ("handle", handle));
            if (status.RootElement.GetProperty("status").GetString() == "ok"
                && status.RootElement.GetProperty("isPaused").GetBoolean())
            {
                parked = JsonDocument.Parse(status.RootElement.GetRawText());
                break;
            }

            await Task.Delay(25);
        }

        Assert.IsNotNull(parked, "the run must report itself held at a node boundary");
        parked.Dispose();

        using var resumed = await InvokeAsync(session, "ResumeCompiledRun", ("handle", handle));
        Assert.AreEqual("ok", resumed.RootElement.GetProperty("status").GetString());

        using var ended = await WaitForEndAsync(session, handle);
        Assert.AreEqual("Completed", ended.RootElement.GetProperty("outcome").GetString());
        Assert.IsFalse(ended.RootElement.GetProperty("isPaused").GetBoolean());
    }

    [TestMethod]
    public async Task AStoppedRun_EndsCancelled_AndContinuesFromItsCheckpoint()
    {
        using var fixture = new TestArchive();
        var archive = WriteChain(fixture, count: 8);
        await using var session = CreateSession(archive);

        using var started = await InvokeAsync(session, "StartCompiledWorkflow", ("startNodeIndex", 0));
        var handle = started.RootElement.GetProperty("handle").GetString()!;

        // Let a couple of nodes finish first: a stop before the first boundary leaves nothing to carry on from.
        await Task.Delay(400);
        using var stopped = await InvokeAsync(session, "StopCompiledRun", ("handle", handle));
        Assert.AreEqual("ok", stopped.RootElement.GetProperty("status").GetString());

        using var cancelled = await WaitForEndAsync(session, handle);
        Assert.AreEqual("Cancelled", cancelled.RootElement.GetProperty("outcome").GetString());

        // The stopped run left a checkpoint in the session's store; continuing is the tool's own carry-on path.
        using var continued = await InvokeAsync(session, "ContinueCompiledWorkflow", ("startNodeIndex", 0));
        Assert.AreEqual("ok", continued.RootElement.GetProperty("status").GetString(), continued.RootElement.ToString());
        var continuedHandle = continued.RootElement.GetProperty("handle").GetString()!;

        using var ended = await WaitForEndAsync(session, continuedHandle);
        Assert.AreEqual("Completed", ended.RootElement.GetProperty("outcome").GetString());
    }

    [TestMethod]
    public async Task AnErrorRedirect_ReRunsFromTheUpstreamNodeAndCompletes()
    {
        using var fixture = new TestArchive();
        var archive = WriteChain(fixture, count: 2, flakyTail: true);
        await using var host = WorkflowSessionHost.Create(Options(
            archive, "--allow-execution", "--in-process-execution",
            "--assembly", typeof(SlowNodeViewModel).Assembly.Location));
        var session = host.Current;
        FlakyHelper.ResetAttempts();

        // Redirect tools are registered next to the session, not in the toolkit; exercise the CLI wrapper itself.
        using var redirect = JsonDocument.Parse(new RedirectTools(host).SetRedirectOnError(0));
        Assert.AreEqual("ok", redirect.RootElement.GetProperty("status").GetString());

        using var result = await InvokeAsync(session, "RunCompiledWorkflow", ("startNodeIndex", 0));
        Assert.AreEqual("ok", result.RootElement.GetProperty("status").GetString(), result.RootElement.ToString());
        Assert.AreEqual("Completed", result.RootElement.GetProperty("outcome").GetString(), result.RootElement.ToString());
        Assert.IsTrue(result.RootElement.GetProperty("attempts").GetInt32() >= 2,
            "the failing node must have been driven again after the redirect");
        Assert.IsTrue(result.RootElement.GetProperty("failures").GetArrayLength() > 0,
            "the first failure stays in the record even though the run completed");
    }
}
