using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Safety;
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
    public void InBypass_TheCallCountLimitsAreNotEnforced()
    {
        // The mode's promise is that nothing is put to the user. A count the model could only get past by
        // asking is a dialog it cannot reach, so the three call-count gates stand down — while the counters
        // keep moving, because sub-agent grants are computed from them.
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree)
            .WithMaxToolCalls(0)
            .WithMaxReadToolCalls(0)
            .WithMaxWriteToolCalls(0)
            .WithPermissionMode(AgentPermissionMode.Bypass);

        var query = JObject.Parse(Invoke(scope, "ValidateWorkflow"));
        Assert.AreEqual("ok", query["status"]?.Value<string>(),
            $"a query must run past a spent count in Bypass: {query}");

        var mutation = JObject.Parse(Invoke(scope, "MarkDirty"));
        Assert.AreNotEqual("error", mutation["status"]?.Value<string>(),
            $"a mutation must run past a spent count in Bypass: {mutation}");

        Assert.AreEqual(0, scope.CreateToolkit().CallUsage.ToolCalls,
            "Bypass does not record spend either, so nothing the tree does depletes a sub-agent allowance");
    }

    [TestMethod]
    public void InBypass_ASpentCallCount_IsNothingToExtend()
    {
        // The reset tool asks about a limit the gate is holding shut. In Bypass the count is not one, so
        // putting the question to the user would reopen nothing.
        var scope = SpendBudget(0).WithPermissionMode(AgentPermissionMode.Bypass);
        var asked = 0;
        scope.WithConfirmationHandler(args =>
        {
            asked++;
            args.Result = AgentConfirmationResult.AllowOnce;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        var json = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual(0, asked, "there is no enforced limit for the user to extend");
        Assert.AreEqual("ok", json["status"]?.Value<string>());
        StringAssert.Contains(json["message"]?.Value<string>() ?? string.Empty, "nothing to extend");
    }

    [TestMethod]
    public void InBypass_TheTokenBudgetIsStillEnforced()
    {
        // A token cap is a spend limit, not a call count: Bypass turns off the requests, not the ceiling the
        // host put on what the conversation may cost.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithMaxTokens(100)
            .WithPermissionMode(AgentPermissionMode.Bypass);
        scope.AddUsage(new UsageDetails { InputTokenCount = 60, OutputTokenCount = 40, TotalTokenCount = 100 });

        var refused = JObject.Parse(Invoke(scope, "ValidateWorkflow"));
        Assert.AreEqual("error", refused["status"]?.Value<string>());
        StringAssert.Contains(refused["message"]!.Value<string>()!, "Token budget");

        var asked = 0;
        scope.WithConfirmationHandler(args =>
        {
            asked++;
            args.Result = AgentConfirmationResult.AllowOnce;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        var reset = JObject.Parse(Invoke(scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual(1, asked, "the token cap still puts its question to the user");
        Assert.AreEqual("ok", reset["status"]?.Value<string>());
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

    /// <summary>
    /// Usage arrives two ways — on the final turn event, and separately for the streaming path — and both
    /// must reach the ledger, or a streamed turn would cost nothing against the cap.
    /// </summary>
    [TestMethod]
    public void ReportedTokens_AccumulateFromBothPipelineEvents()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        scope.Pipeline.PublishAsync(new VeloxDev.AI.Pipelines.AgentUsageReported(
            new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 }))
            .AsTask().GetAwaiter().GetResult();
        scope.Pipeline.PublishAsync(new AgentTurnCompleted(
            VeloxDev.AI.Pipelines.AgentRunKind.Complete, "done", TimeSpan.Zero,
            new UsageDetails { InputTokenCount = 20, OutputTokenCount = 10, TotalTokenCount = 30 }))
            .AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(30, scope.InputTokens);
        Assert.AreEqual(15, scope.OutputTokens);
        Assert.AreEqual(45, scope.TotalTokens);
    }

    [TestMethod]
    public void TheTokenCap_RefusesCallsOnceTheReportedSpendReachesIt()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMaxTokens(100);
        scope.AddUsage(new UsageDetails { InputTokenCount = 60, OutputTokenCount = 40, TotalTokenCount = 100 });

        var refused = JObject.Parse(Invoke(scope, "ValidateWorkflow"));

        Assert.AreEqual("error", refused["status"]?.Value<string>());
        StringAssert.Contains(refused["message"]!.Value<string>()!, "Token budget");
    }

    [TestMethod]
    public void ResettingTokenUsage_ReopensTheTokenCap()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMaxTokens(100);
        scope.AddUsage(new UsageDetails { TotalTokenCount = 100 });
        Assert.AreEqual(100, scope.TotalTokens);

        scope.ResetTokenUsage();

        Assert.AreEqual(0, scope.TotalTokens);
        Assert.AreEqual("ok", JObject.Parse(Invoke(scope, "ValidateWorkflow"))["status"]?.Value<string>(),
            "with the ledger cleared, the next call is accepted again");
    }

    [TestMethod]
    public void InBypass_ThePromptDoesNotStateCallCapsAsAWall()
    {
        // The envelope must agree with the gate. Printing "cap 10" in Bypass describes a wall the model
        // cannot hit, and a model that believes it is one call from the end stops early for no reason.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithMaxToolCalls(10)
            .WithPermissionMode(AgentPermissionMode.Bypass);

        // The budget block is in the per-turn capability envelope, not the frozen skeleton prompt.
        var envelope = new WorkflowAgentContextProvider(scope).BuildContext().Instructions!;

        StringAssert.Contains(envelope, "not enforced in Bypass mode");
        Assert.IsFalse(envelope.Contains("Tool calls: cap 10"),
            "the cap must not be described as active in the mode that does not enforce it");
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
