using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Coverage for the way out of a spent tool-call budget.
/// <para>
/// Before this existed a limit was terminal: the gate refused every tool, including any that could have
/// reopened it, and nothing in the prompt said so — the run simply stopped mid-task with no explanation.
/// </para>
/// <para>
/// The safety property is the point: the Agent may <i>ask</i>, and only the user's agreement reopens the
/// budget. A host that never enabled confirmations cannot be talked into it.
/// </para>
/// </summary>
[TestClass]
public class BudgetResetTests
{
    private static string Invoke(WorkflowAgentScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = scope.ProvideTools().OfType<AIFunction>()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not registered.");
        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null) aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result switch
        {
            string s => s,
            JsonElement je => je.GetString() ?? string.Empty,
            _ => result?.ToString() ?? string.Empty,
        };
    }

    private static WorkflowAgentScope SpendBudget(int limit = 1)
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        return new WorkflowAgentScope(tree).WithMaxToolCalls(limit);
    }

    [TestMethod]
    public void TheResetIsOfferedEvenOnAShrunkenToolSurface()
    {
        // A host that narrowed the surface to save tokens must still have a way out of a budget it set:
        // the reset is added outside the category filter for exactly that reason.
        var scope = SpendBudget(10);

        var names = scope.ProvideTools(WorkflowToolCategory.Query).Select(t => t.Name).ToArray();

        CollectionAssert.Contains(names, WorkflowAgentToolkit.ResetBudgetToolName);
        CollectionAssert.DoesNotContain(names, "CreateNode", "the category filter still applies to everything else");
    }

    [TestMethod]
    public void ARefusal_NamesTheWayOut()
    {
        // The model cannot see the limit itself — only this message — so a refusal that says only "no
        // further calls" is where a run used to stop without explanation.
        var scope = SpendBudget(0);

        var refusal = Invoke(scope, "GetWorkflowSummary");

        StringAssert.Contains(refusal, "limit");
        StringAssert.Contains(refusal, WorkflowAgentToolkit.ResetBudgetToolName,
            "a refusal must point at the tool that can reopen the budget");
    }

    [TestMethod]
    public void WithNoConfirmationHandler_TheBudgetStaysClosed()
    {
        // An unanswerable prompt denies. The Agent must not be able to widen its own budget by calling a
        // tool a host never agreed to answer.
        var scope = SpendBudget(0);

        var json = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual("denied", json["status"]?.Value<string>());
        Assert.IsTrue((json["message"]?.Value<string>() ?? string.Empty).Contains("did not allow"));
    }

    [TestMethod]
    public void WhenTheUserRefuses_TheBudgetStaysClosed()
    {
        var scope = SpendBudget(0);
        var asked = 0;
        scope.WithConfirmationHandler(args =>
        {
            asked++;
            args.Result = AgentConfirmationResult.Deny;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        var json = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual(1, asked, "the user must be asked before anything is reopened");
        Assert.AreEqual("denied", json["status"]?.Value<string>());
    }

    [TestMethod]
    public void WhenTheUserAgrees_TheBudgetReopens()
    {
        var scope = SpendBudget(1);
        scope.WithConfirmationHandler(args =>
        {
            args.Result = AgentConfirmationResult.AllowOnce;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        // ValidateWorkflow rather than GetWorkflowSummary: not every tool answers with a `status`, and this
        // test is about the budget, not about that. Each assertion carries the raw answer so a failure
        // says what came back rather than only which field was missing.
        var first = Invoke(scope, "ValidateWorkflow");
        Assert.AreEqual("ok", JObject.Parse(first)["status"]?.Value<string>(), $"1st: {first}");

        var second = Invoke(scope, "ValidateWorkflow");
        Assert.AreEqual("error", JObject.Parse(second)["status"]?.Value<string>(), $"2nd (must be refused): {second}");

        var reset = Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName);
        Assert.AreEqual("ok", JObject.Parse(reset)["status"]?.Value<string>(), $"reset: {reset}");

        var third = Invoke(scope, "ValidateWorkflow");
        Assert.AreEqual("ok", JObject.Parse(third)["status"]?.Value<string>(), $"3rd (must be accepted again): {third}");
    }

    [TestMethod]
    public void Always_IsAskedOnceAndCoversTheRestOfTheSession()
    {
        // The key has to be stable, or "always" would not stick and the user would be asked again the next
        // time the same budget runs out.
        var scope = SpendBudget(0);
        var keys = new System.Collections.Generic.List<string>();
        scope.WithConfirmationHandler(args =>
        {
            keys.Add(args.OperationKey);
            args.Result = AgentConfirmationResult.AllowAlways;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        var first = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));
        var second = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.HasCount(1, keys, "the session-wide approval must be remembered");
        Assert.AreEqual(WorkflowAgentToolkit.ResetBudgetOperationKey, keys[0]);
        Assert.AreEqual("ok", first["status"]?.Value<string>());
        // The second call finds no limit reached — the first one already reopened it — so it answers
        // without a fresh budget and without asking.
        Assert.AreEqual("ok", second["status"]?.Value<string>());
    }

    [TestMethod]
    public void AtInteractionLevelZero_NobodyIsAsked_AndTheBudgetStaysClosed()
    {
        // Level 0 is the host saying "never interrupt me". There is then no way to obtain the user's
        // agreement, and a budget may only be reopened with it — so this must deny, not ask anyway.
        var scope = SpendBudget(0).WithInteractionSafety(0);
        var asked = 0;
        scope.WithConfirmationHandler(args =>
        {
            asked++;
            args.Result = AgentConfirmationResult.AllowOnce;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        var json = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual(0, asked, "the host asked not to be interrupted");
        Assert.AreEqual("denied", json["status"]?.Value<string>());
    }

    [TestMethod]
    public void WhenNoLimitIsSet_ThereIsNothingToExtend()
    {
        // No budget configured: the tool answers rather than asking the user a pointless question.
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree);
        var asked = 0;
        scope.WithConfirmationHandler(args =>
        {
            asked++;
            args.Result = AgentConfirmationResult.AllowOnce;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        var json = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual("ok", json["status"]?.Value<string>());
        Assert.AreEqual(0, asked, "there is no budget to extend, so nobody should be asked");
    }

    [TestMethod]
    public void TheResetIsNotCountedAgainstTheMutationBudget()
    {
        // It touches the budget, not the workflow. Classified as a mutation it would spend a mutation slot
        // — and, with auto-dirty on, mark a graph it never touched.
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree).WithMaxWriteToolCalls(1);
        scope.WithConfirmationHandler(args =>
        {
            args.Result = AgentConfirmationResult.AllowOnce;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName);

        // The single mutation slot must still be there for a real mutation.
        var json = JObject.Parse(Invoke(scope, "MarkDirty"));
        Assert.AreNotEqual("error", json["status"]?.Value<string>(),
            "reopening the budget must not spend a mutation slot");
    }

    [TestMethod]
    public void ThePrompt_TellsTheAgentToAskRatherThanRetry()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMaxToolCalls(10);
        var prompt = scope.ProvideProgressiveContextPrompt();

        StringAssert.Contains(prompt, WorkflowAgentToolkit.ResetBudgetToolName);
        StringAssert.Contains(prompt, "Do not retry the refused tool");
        StringAssert.Contains(prompt, "only their agreement reopens the budget");
    }
}
