using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Coverage for the capability envelope — the prompt text <see cref="WorkflowAgentContextProvider"/>
/// contributes on every turn.
/// <para>
/// The host freezes <see cref="WorkflowAgentScope.ProvideProgressiveContextPrompt"/> into
/// <c>ChatOptions.Instructions</c> once, at construction. Everything the scope can be reconfigured with
/// afterwards therefore reached the tools and never the prompt, leaving the model working from a stale
/// description of what it was allowed to do. These tests pin both halves of the fix: that the envelope
/// states the live state, and that it stays silent about anything the frozen skeleton already said.
/// </para>
/// </summary>
[TestClass]
public class CapabilityEnvelopeTests
{
    /// <summary>The text the provider contributes for this scope, as the model would receive it.</summary>
    private static string Envelope(WorkflowAgentScope scope)
        => new WorkflowAgentContextProvider(scope).BuildContext().Instructions!;

    private static WorkflowAgentScope Scope() => new(new TreeDefaultViewModel());

    /// <summary>Calls a tool by the name it reaches the model under, the way a host's run would.</summary>
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

    // ── Gates ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Gates_SayWhetherNodeExecutionIsAllowed()
    {
        // Both defaults and both flips: the tool descriptions state that node execution is "disabled by
        // default" no matter which way it is set, so the envelope is the only place the model can learn
        // the truth.
        StringAssert.Contains(Envelope(Scope()), "DENIED");
        StringAssert.Contains(Envelope(Scope().WithAllowNodeExecution(true)), "ALLOWED");
    }

    [TestMethod]
    public void Gates_SayWhichGenericCommandsAreAllowlisted()
    {
        StringAssert.Contains(Envelope(Scope()), "disabled entirely");

        var allowlisted = Envelope(Scope().WithAllowedGenericCommands("MoveNode"));

        StringAssert.Contains(allowlisted, "MoveNodeCommand",
            "the name as it reaches the model, not the bare name the host passed");
    }

    [TestMethod]
    public void Gates_SayWhichWayDirtyMarkingIsConfigured()
    {
        // CommandReference.md documents both modes side by side and cannot say which one is live, so a
        // model that guesses wrong either marks dirty twice or not at all.
        StringAssert.Contains(Envelope(Scope()), "Automatic dirty marking: **OFF");
        StringAssert.Contains(Envelope(Scope().WithAutoMarkDirty(true)), "Automatic dirty marking: **ON");
    }

    [TestMethod]
    public void SwitchedOffTools_AreNamedAsNotOffered()
    {
        // The tool is gone from the surface entirely, so naming it is a deliberate trade: the model would
        // otherwise rediscover it by failing, and re-try it every few turns.
        var scope = Scope();
        Assert.IsTrue(scope.SetToolEnabled("ListNodes", false));

        var envelope = Envelope(scope);

        StringAssert.Contains(envelope, "### Tools switched off by the host");
        StringAssert.Contains(envelope, "`ListNodes`");
        Assert.IsFalse(scope.ProvideTools().OfType<AIFunction>().Any(t => t.Name == "ListNodes"),
            "and it really must be absent, not merely described as absent");
    }

    // ── Budgets ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Budgets_StateTheCapWhileStayingSilentAboutUnusedHeadroom()
    {
        var envelope = Envelope(Scope().WithMaxToolCalls(10));

        StringAssert.Contains(envelope, "Tool calls: cap 10");
        Assert.IsFalse(envelope.Contains("0/10"),
            "a budget nobody has touched is not news, and a raw counter would change the prompt every turn");
    }

    [TestMethod]
    public void Budgets_WarnOnlyOnceTheUsageIsNearTheCap()
    {
        // The reason the envelope bands instead of counting: the existing contract is that an unchanged
        // scope renders identical text, and a raw count would break it on every single tool call.
        var scope = Scope().WithMaxToolCalls(10);
        var fresh = Envelope(scope);

        for (int i = 0; i < 7; i++) Invoke(scope, "ListNodes");
        Assert.AreEqual(fresh, Envelope(scope), "70% of a budget is not worth a warning");

        Invoke(scope, "ListNodes");
        var warned = Envelope(scope);

        Assert.AreNotEqual(fresh, warned, "80% is");
        StringAssert.Contains(warned, "8/10");
        StringAssert.Contains(warned, "near the limit");
    }

    [TestMethod]
    public void Budgets_StateTheSplitSoTheModelDoesNotThinkOneBudgetCoversAll()
    {
        // The frozen failure protocol says every tool goes through one budget, which stops being true the
        // moment a class-specific cap is set. Without the split stated, the model reads "the read budget is
        // spent" and gives up on mutations too — or the reverse.
        var envelope = Envelope(Scope().WithMaxToolCalls(100).WithMaxReadToolCalls(4).WithMaxWriteToolCalls(8));

        StringAssert.Contains(envelope, "Read-only (query) calls: cap 4");
        StringAssert.Contains(envelope, "Mutating calls: cap 8");
    }

    [TestMethod]
    public void CapAndSwitchSetters_AdvanceTheVersion()
    {
        // Version is the cache key. A setter that changes what the envelope says without moving it leaves
        // the model reading the old text forever — and, for the caps, disagreeing with the gate that
        // enforces them, which reads the property live. All four of these used to be silent.
        var scope = Scope();

        AssertVersionAdvances(scope, s => s.WithMaxToolCalls(10));
        AssertVersionAdvances(scope, s => s.WithMaxReadToolCalls(4));
        AssertVersionAdvances(scope, s => s.WithMaxWriteToolCalls(8));
        AssertVersionAdvances(scope, s => s.WithAutoMarkDirty(true));
    }

    /// <summary>Applies a configuration and fails unless it moved <see cref="WorkflowAgentScope.Version"/>.</summary>
    private static void AssertVersionAdvances(
        WorkflowAgentScope scope, Func<WorkflowAgentScope, WorkflowAgentScope> configure)
    {
        var before = scope.Version;
        configure(scope);
        Assert.IsGreaterThan(before, scope.Version);
    }

    // ── Drift: what the skeleton already said must not be said twice ────────

    [TestMethod]
    public void NoDrift_MeansTheEnvelopeDoesNotRestateTheSkeleton()
    {
        var scope = Scope();
        var skeleton = scope.ProvideProgressiveContextPrompt();
        StringAssert.Contains(skeleton, "## Interaction Safety Policy", "the skeleton is what carries the policy");

        var envelope = Envelope(scope);

        Assert.IsFalse(envelope.Contains("## Interaction Safety Policy"),
            "the receipt exists precisely so a section the skeleton already wrote is not written again");
    }

    [TestMethod]
    public void WithoutASkeleton_TheEnvelopeStandsAlone()
    {
        // A host can attach the provider without ever rendering a skeleton. Nothing has been stated, so
        // there is nothing to supersede and the envelope must carry the whole policy itself.
        var envelope = Envelope(Scope());

        StringAssert.Contains(envelope, "## Interaction Safety Policy");
        StringAssert.Contains(envelope, "Active safety level: **1**");
        Assert.IsFalse(envelope.Contains("written for level"),
            "nothing was frozen, so no replacement wording belongs here");
    }

    [TestMethod]
    public async Task SafetyLevelChangedAfterConstruction_ReachesTheModelAsAReplacement()
    {
        // The bug this whole change exists for, asserted end to end: the host renders the skeleton, builds
        // the agent, and only then turns the safety level down. Before the envelope, the model kept reading
        // the level-1 policy and the level-1 number for the rest of the run.
        var scope = Scope();
        var skeleton = scope.ProvideProgressiveContextPrompt();
        scope.WithInteractionSafety(3);

        var client = await OfflineAgent.RunOnce(scope, skeleton);

        var prose = string.Join("\n", client.Prose);
        StringAssert.Contains(prose, "written for level 1", "the model has to be told the older block is dead");
        StringAssert.Contains(prose, "Active safety level: **3**");
    }

    [TestMethod]
    public void SafetyLevelTurnedOffAfterConstruction_SaysThePolicyWasWithdrawn()
    {
        // Level 0 builds no policy text at all. Emitting the replacement heading with nothing under it
        // would read as "the rules are gone", which is not the same as "there are none to begin with".
        var scope = Scope();
        scope.ProvideProgressiveContextPrompt();
        scope.WithInteractionSafety(0);

        var envelope = Envelope(scope);

        StringAssert.Contains(envelope, "Interaction safety policy withdrawn");
        Assert.IsFalse(envelope.Contains("**the following replaces it**"));
    }

    [TestMethod]
    public void CustomToolGuidanceAddedAfterConstruction_ReachesTheModelAsADelta()
    {
        // WithTools refreshes the tool list every turn, so the tool arrived while the text explaining when
        // to use it stayed behind in the frozen skeleton — a tool the model had no instructions for.
        var scope = Scope();
        var skeleton = scope.ProvideProgressiveContextPrompt();
        Assert.IsFalse(skeleton.Contains("use WidgetTool for widgets"));

        scope.WithTools("use WidgetTool for widgets", AIFunctionFactory.Create(() => "ok", "WidgetTool"));

        var envelope = Envelope(scope);

        StringAssert.Contains(envelope, "use WidgetTool for widgets");
        StringAssert.Contains(envelope, "since startup");
    }

    [TestMethod]
    public void TypesRegisteredAfterConstruction_AppearAsADelta()
    {
        // The skeleton tells the model to call GetComponentContext for every type it operates on, but a
        // type registered later is in no catalogue the model can see — so it never asks about it.
        var scope = Scope();
        scope.ProvideProgressiveContextPrompt();

        scope.WithData([typeof(CapabilityEnvelopeTests)]);

        var envelope = Envelope(scope);

        StringAssert.Contains(envelope, "Types registered since startup");
        StringAssert.Contains(envelope, typeof(CapabilityEnvelopeTests).FullName!);
    }

    [TestMethod]
    public void TypesRegisteredBeforeConstruction_AreNotRepeated()
    {
        var scope = Scope().WithData([typeof(CapabilityEnvelopeTests)]);
        scope.ProvideProgressiveContextPrompt();

        Assert.IsFalse(Envelope(scope).Contains("Types registered since startup"),
            "the skeleton listed it already");
    }

    // ── Shape ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheEnvelope_StaysAnOrderOfMagnitudeSmallerThanTheSkeleton()
    {
        // The skeleton costs ~870 KB to render and must never run per turn; the envelope is what runs
        // instead. If it ever grows to skeleton scale this design has lost the thing it was built for, so
        // the ratio — not the absolute size — is what is pinned.
        var scope = Scope();
        var skeleton = scope.ProvideProgressiveContextPrompt();

        var envelope = Envelope(scope);

        Assert.IsLessThan(skeleton.Length / 10, envelope.Length,
            $"envelope {envelope.Length} chars vs skeleton {skeleton.Length}");
    }

    [TestMethod]
    public void TheEnvelope_FollowsTheSkeletonLanguage()
    {
        var scope = Scope().WithPromptLanguage(AgentLanguages.Chinese);
        scope.ProvideProgressiveContextPrompt();

        StringAssert.Contains(Envelope(scope), "当前生效的闸门");
    }

    [TestMethod]
    public void IdleTurns_HandBackTheVerySameContext()
    {
        // Which is the whole performance story: an agent that is thinking, not reconfiguring, adds nothing
        // to the GC. Asserted on the provider rather than the scope because sharing one instance with the
        // framework is the part that needed proving.
        var provider = new WorkflowAgentContextProvider(Scope());

        Assert.AreSame(provider.BuildContext(), provider.BuildContext());
    }

    [TestMethod]
    public void IdleTurns_AddNothingToTheGC()
    {
        // The claim the whole split was built to support, and the reason the envelope bands its budget
        // usage instead of counting: a per-turn counter would make the text differ every turn, and a
        // differing render is a new string — ~750 chars against a framework baseline of tens of kilobytes.
        // Counted per thread, so the suite's method-level parallelism cannot perturb it.
        var provider = new WorkflowAgentContextProvider(Scope());
        provider.BuildContext(); // first render, and warms the toolkit

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) provider.BuildContext();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0L, allocated,
            "an unchanged scope must allocate nothing — not the text, not the tool list, not even the AIContext wrapper");
    }
}
