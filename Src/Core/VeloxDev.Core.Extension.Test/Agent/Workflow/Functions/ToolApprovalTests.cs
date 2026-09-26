using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Covers the human gate on mutating tool calls: <see cref="WorkflowAgentScope.WithToolApproval"/> must put a
/// non-query call to the host's confirmation handler, must let a query through untouched, and must deny when
/// there is nobody to ask.
/// </summary>
[TestClass]
public class ToolApprovalTests
{
    /// <summary>
    /// Invokes a workflow tool through its public registration path — the same route an AI host uses — so the
    /// test exercises the wrapper's gate rather than calling a tool body directly.
    /// </summary>
    private static string InvokeTool(WorkflowAgentScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = scope.ProvideTools()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)) as AIFunction
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not registered.");

        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null)
                aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result switch
        {
            string s => s,
            JsonElement je => je.GetString() ?? string.Empty,
            _ => result?.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// A tree with exactly one mounted node — the smallest thing DeleteNode can act on. <paramref name="approval"/>
    /// has no default on purpose: the first version of this file defaulted it to <c>true</c>, which silently
    /// turned the "gate is off" test into a second denial test.
    /// </summary>
    private static (TreeDefaultViewModel Tree, WorkflowAgentScope Scope) OneNodeScope(
        bool approval, Func<AgentConfirmationEventArgs, Task>? confirm = null)
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new NodeDefaultViewModel());

        var scope = new WorkflowAgentScope(tree).WithToolApproval(approval);
        if (confirm is not null) scope.WithConfirmationHandler(confirm);
        return (tree, scope);
    }

    /// <summary>Off is the default, so a host that never asks for the gate keeps today's behaviour.</summary>
    [TestMethod]
    public void WithoutTheGate_AMutationRunsWithoutAskingAnybody()
    {
        var asked = 0;
        var (tree, scope) = OneNodeScope(approval: false, confirm: e =>
        {
            asked++;
            e.Result = AgentConfirmationResult.AllowOnce;
            return Task.CompletedTask;
        });

        var result = InvokeTool(scope, "DeleteNode", ("nodeIndex", 0));

        Assert.AreEqual("ok", JObject.Parse(result)["status"]?.Value<string>());
        Assert.HasCount(0, tree.Nodes, "the deletion was supposed to happen");
        Assert.AreEqual(0, asked, "the gate is off, so nobody may be asked");
    }

    /// <summary>An approval is asked, answered yes, and the call then runs.</summary>
    [TestMethod]
    public void WithTheGate_AnApprovedMutationRuns()
    {
        AgentConfirmationEventArgs? asked = null;
        var (tree, scope) = OneNodeScope(approval: true, confirm: e =>
        {
            asked = e;
            e.Result = AgentConfirmationResult.AllowOnce;
            return Task.CompletedTask;
        });

        var result = InvokeTool(scope, "DeleteNode", ("nodeIndex", 0));

        Assert.AreEqual("ok", JObject.Parse(result)["status"]?.Value<string>());
        Assert.HasCount(0, tree.Nodes, "an approved call must actually run");
        Assert.IsNotNull(asked, "the host has to be asked before a mutation runs");
        Assert.AreEqual("DeleteNode", asked.OperationKey, "the key is the tool name, which is what a host allows for the session");
    }

    /// <summary>
    /// The point of the gate: a denial stops the call before its body, which is what a model that never calls
    /// RequestConfirmation cannot get around.
    /// </summary>
    [TestMethod]
    public void WithTheGate_ADeniedMutationNeverRunsAndSaysSo()
    {
        var (tree, scope) = OneNodeScope(approval: true, confirm: e =>
        {
            e.Result = AgentConfirmationResult.Deny;
            return Task.CompletedTask;
        });

        var result = InvokeTool(scope, "DeleteNode", ("nodeIndex", 0));

        Assert.AreEqual("error", JObject.Parse(result)["status"]?.Value<string>());
        StringAssert.Contains(result, "not approved");
        Assert.HasCount(1, tree.Nodes, "a denied call must leave the graph exactly as it was");
    }

    /// <summary>Read-only queries are not put to the user: approving a read would be noise.</summary>
    [TestMethod]
    public void WithTheGate_AQueryIsNotPutToTheHost()
    {
        var asked = 0;
        var (tree, scope) = OneNodeScope(approval: true, confirm: e =>
        {
            asked++;
            e.Result = AgentConfirmationResult.AllowOnce;
            return Task.CompletedTask;
        });

        var result = InvokeTool(scope, "GetWorkflowSummary");

        Assert.AreEqual(0, asked, "a query has nothing to approve");
        // The summary carries no status field, so the effect is the assertion: the tool ran and saw the tree.
        Assert.AreEqual(1, JObject.Parse(result)["nodeCount"]?.Value<int>());
        Assert.HasCount(1, tree.Nodes);
    }

    /// <summary>
    /// Nobody to ask means no. The gate is closed by default for the same reason a confirmation dialog
    /// defaults to Deny: an unanswered prompt must never read as permission.
    /// </summary>
    [TestMethod]
    public void WithTheGateAndNoHandler_TheMutationIsDenied()
    {
        var (tree, scope) = OneNodeScope(approval: true);

        var result = InvokeTool(scope, "DeleteNode", ("nodeIndex", 0));

        Assert.AreEqual("error", JObject.Parse(result)["status"]?.Value<string>());
        Assert.HasCount(1, tree.Nodes, "an unanswerable prompt denies");
    }
}
