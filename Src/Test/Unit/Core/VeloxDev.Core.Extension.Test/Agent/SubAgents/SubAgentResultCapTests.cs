using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// A child's report is the one place the Agent asks another model to read a great deal and answer briefly. The
/// result tool still has to bound it: an unbounded report is a context-sized payload arriving through the very
/// delegation that was supposed to keep the parent's context small.
/// </summary>
[TestClass]
public class SubAgentResultCapTests
{
    private static string Invoke(WorkflowAgentScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = SubAgentFixture.SubAgentToolOf(scope, toolName);
        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null) aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    private static string Spawn(WorkflowAgentScope scope)
    {
        var reply = JObject.Parse(Invoke(scope, "SpawnSubAgent", ("task", "report the node count")));
        Assert.AreEqual("ok", reply["status"]?.Value<string>(), reply.ToString());
        return reply["id"]!.Value<string>()!;
    }

    [TestMethod]
    public async Task GetSubAgentResult_CapsTheReport_AndSaysItDid()
    {
        const int answerLength = 400;
        await using var fx = new SubAgentFixture(client: new InstantChatClient(new string('x', answerLength)));
        var id = Spawn(fx.Scope);
        SubAgentFixture.WaitFor(() => fx.Rows.All(r => r.IsFinished));

        var uncapped = JObject.Parse(Invoke(fx.Scope, "GetSubAgentResult", ("id", id)));
        Assert.AreEqual(answerLength, (uncapped["result"]?.Value<string>() ?? string.Empty).Length);
        Assert.IsNull(uncapped["truncated"], "an under-limit report is not marked truncated");

        var capped = JObject.Parse(Invoke(fx.Scope, "GetSubAgentResult", ("id", id), ("maxChars", 100)));
        var text = capped["result"]?.Value<string>() ?? string.Empty;
        Assert.AreEqual("ok", capped["status"]?.Value<string>(), capped.ToString());
        Assert.IsTrue(text.StartsWith(new string('x', 100), StringComparison.Ordinal),
            "the cap keeps the first 100 characters of the report");
        Assert.IsTrue(text.Length > 100, "the truncation note follows the kept text");
        Assert.AreEqual(true, capped["truncated"]?.Value<bool>(), "the model must know the report was cut");
    }
}
