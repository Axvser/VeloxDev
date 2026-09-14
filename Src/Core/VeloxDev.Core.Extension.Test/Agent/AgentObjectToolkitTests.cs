using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VeloxDev.AI;

namespace VeloxDev.Core.Extension.Test.Agent;

/// <summary>
/// Coverage for <see cref="AgentObjectToolkit"/>. It shares the tracked wrapper with the workflow and
/// subsystem toolkits, so these lock the behaviour it contributes to that wrapper: one global call
/// ceiling, a <c>ToolCalled</c> notification per call, and no thread marshalling of its own.
/// </summary>
[TestClass]
public class AgentObjectToolkitTests
{
    /// <summary>The object under test — records how often its method actually ran.</summary>
    public sealed class Probe
    {
        public int Calls { get; private set; }

        public string Echo(string message)
        {
            Calls++;
            return message;
        }
    }

    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            callArgs[name] = value;

        var result = ((AIFunction)tool)
            .InvokeAsync(callArgs, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    private static AITool Tool(AgentObjectToolkit toolkit, string name)
        => toolkit.CreateTools().Single(t => t.Name == name);

    [TestMethod]
    public void CreateTools_RegistersEveryToolWithTheSharedWrapper()
    {
        // Every tool must go through the wrapper, or it loses accounting and notification together.
        var tools = new AgentObjectToolkit(new Probe()).CreateTools();

        Assert.IsNotEmpty(tools);
        foreach (var tool in tools)
            Assert.AreEqual("TrackedAIFunction", tool.GetType().Name, $"'{tool.Name}' is not wrapped");
    }

    [TestMethod]
    public void ToolCalled_IsRaisedWithACumulativeCount()
    {
        var toolkit = new AgentObjectToolkit(new Probe());
        var seen = new List<AgentToolCallEventArgs>();
        toolkit.ToolCalled += (_, e) => seen.Add(e);

        Invoke(Tool(toolkit, "GetComponentInfo"));
        Invoke(Tool(toolkit, "GetComponentInfo"));

        Assert.HasCount(2, seen);
        Assert.AreEqual("GetComponentInfo", seen[0].ToolName);
        Assert.AreEqual(1, seen[0].CallCount);
        Assert.AreEqual(2, seen[1].CallCount, "the count is cumulative, not per tool");
        Assert.Contains("Probe", seen[0].Result, "the result text reaches the notification");
    }

    [TestMethod]
    public void MaxToolCalls_RefusesRatherThanThrows()
    {
        var toolkit = new AgentObjectToolkit(new Probe()) { MaxToolCalls = 1 };

        // The first call is a normal result — note these tools do not carry a `status` field, so the
        // refusal has to be recognised by its own shape rather than by contrast with a success status.
        var allowed = JObject.Parse(Invoke(Tool(toolkit, "GetComponentInfo")));
        Assert.IsNotNull(allowed["type"], "the tool ran and produced its normal payload");
        Assert.IsNull(allowed["status"]);

        var refused = JObject.Parse(Invoke(Tool(toolkit, "GetComponentInfo")));
        Assert.AreEqual("error", refused["status"]?.Value<string>());
        Assert.Contains("limit", refused["message"]?.Value<string>() ?? string.Empty);
    }

    [TestMethod]
    public void Refusal_HappensBeforeTheToolRuns()
    {
        // A refusal that still invoked the tool would be a gate in name only — the point is that the
        // call never reaches the target.
        var probe = new Probe();
        var toolkit = new AgentObjectToolkit(probe) { MaxToolCalls = 1 };

        Invoke(Tool(toolkit, "InvokeMethod"), ("methodName", "Echo"), ("jsonArgs", "[\"first\"]"));
        Assert.AreEqual(1, probe.Calls);

        Invoke(Tool(toolkit, "InvokeMethod"), ("methodName", "Echo"), ("jsonArgs", "[\"second\"]"));
        Assert.AreEqual(1, probe.Calls, "a refused call must not reach the target object");
    }

    [TestMethod]
    public void ThrowingTool_BecomesAnErrorResult()
    {
        var toolkit = new AgentObjectToolkit(new Probe());

        // No such method: the invoker reports the failure itself, which the wrapper must pass through.
        var json = JObject.Parse(Invoke(Tool(toolkit, "InvokeMethod"), ("methodName", "Nope"), ("jsonArgs", "[]")));

        Assert.AreEqual("error", json["status"]?.Value<string>());
    }

    [TestMethod]
    public void WithoutAContext_TheToolRunsOnTheCallingThread()
    {
        // The toolkit registers no marshalling: an arbitrary object has no thread it must run on.
        var callerThread = Environment.CurrentManagedThreadId;
        int toolThread = 0;

        var toolkit = new AgentObjectToolkit(new Target(line =>
        {
            toolThread = Environment.CurrentManagedThreadId;
            return line;
        }));
        Invoke(Tool(toolkit, "InvokeMethod"), ("methodName", "Echo"), ("jsonArgs", "[\"x\"]"));

        Assert.AreEqual(callerThread, toolThread);
    }

    /// <summary>Same shape as <see cref="Probe"/>, but records the calling thread.</summary>
    public sealed class Target(Func<string, string> onEcho)
    {
        public string Echo(string message) => onEcho(message);
    }
}
