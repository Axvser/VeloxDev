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
/// A compiled run's payload is reported even when the archive format cannot carry it.
/// </summary>
/// <remarks>
/// <para>
/// <c>context.Data</c> is the host's own business object — a node's output, whatever type that happens to be.
/// The tool rendered it with <c>VeloxJsonValue.From</c>, which falls through to the archive serializer and
/// therefore throws <c>MissingWriter</c> for any type outside the generated closure. There was no guard, so the
/// throw escaped into the tracked wrapper and the whole status call came back an error envelope: the Agent asked
/// "how did the run end" and was told the tool had failed, over a payload it only wanted to look at.
/// </para>
/// <para>
/// The demo never showed it: its nodes answer in JSON, so its payload happens to be archivable. The timer helper
/// below puts a type the archive will never carry (<see cref="Version"/>) where the run's data goes — which is
/// what a host's own result object looks like on the day it is a class of its own.
/// </para>
/// </remarks>
[TestClass]
public class CompiledRunPayloadTests
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
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-agent-payload-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    /// <summary>The demo's ticker, answering with the host's own result object instead of a timestamp.</summary>
    private sealed class PayloadTimerHelper : TimerHelper
    {
        private readonly object _payload;

        public PayloadTimerHelper(object payload) => _payload = payload;

        public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
            => Task.FromResult<object?>(_payload);
    }

    [TestMethod]
    public void Status_ReportsAPayloadTheArchiveCannotCarry_InsteadOfFailing()
    {
        var session = WorkflowDemoSession.Create(Scratch());
        _sessions.Add(session);

        // The payload the archive has no writer for — neither a scalar, a container, a component nor annotated,
        // which is the shape of a host's own result type.
        var payload = new Version(1, 2);

        // The ticker is the demo's data source: it is what puts a value into the session's Data. Answering with
        // the payload makes the run's data exactly the object the tool has to render.
        session.Tree.Nodes.OfType<TimerNodeViewModel>().Single().SetHelper(new PayloadTimerHelper(payload));

        // Stop the run at the first Python node, so nothing downstream can replace the payload before it is read.
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
            node.SetHelper(new StubPythonHelper { CancelAtInvocation = 1 });

        var startIndex = session.Tree.Nodes.ToList().FindIndex(n => n is ControllerViewModel);
        var scope = session.Tree.AsAgentScope().WithAllowNodeExecution(true);

        var started = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "StartCompiledWorkflow",
            ("startNodeIndex", startIndex)));
        Assert.AreEqual("ok", started["status"]?.Value<string>(), started.ToString());
        var handle = started["handle"]!.Value<string>()!;

        var status = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "GetCompiledRunStatus", ("handle", handle)));

        Assert.AreEqual("ok", status["status"]?.Value<string>(),
            "a payload the archive cannot write must not turn the status call into an error: " + status);
        Assert.IsNotNull(status["outcome"], "and the answer the Agent actually asked for is still there");

        var data = status["data"];
        Assert.AreEqual(JTokenType.String, data?.Type,
            "the payload is reported as its text rather than dropped: " + status);
        Assert.AreEqual("1.2", data!.ToString(), "and the text is the payload's own");
    }
}
