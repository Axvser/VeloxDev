using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Coverage for the Agent Framework's own capability providers, as
/// <see cref="WorkflowAgentScope"/> attaches them: the todo list, the operating modes, and context
/// compaction.
/// <para>
/// These are the framework's implementations, not this repo's, so what is asserted here is the
/// <i>attachment</i> — that attaching one actually reaches the model, and that it lands where the
/// composition promises. Re-testing the framework's own behaviour would only pin its version.
/// </para>
/// </summary>
[TestClass]
public class AgentCapabilityProvidersTests
{
    // ── Todo ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task WithTodoTracking_ReachesTheModelWithTheFrameworksTodoTools()
    {
        // The whole point of attaching it: the tools are the framework's, and they arrive without this
        // repo listing them anywhere.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithTodoTracking();

        var client = await OfflineAgent.RunOnce(scope);

        CollectionAssert.IsSubsetOf(
            new[] { "todos_add", "todos_complete", "todos_remove", "todos_get_remaining", "todos_get_all" },
            client.ToolNames,
            "the todo provider's tools must be offered to the model");

        Assert.IsTrue(client.Prose.Any(p => p.Contains("Todo Items")),
            "the provider's instructions must reach the model too — tools without the guidance are just five more names");
    }

    [TestMethod]
    public void WithTodoTracking_ExposesTheProviderForAHostToBind()
    {
        // The list lives in the framework's provider and there is no other way to reach it, so a host that
        // wants to show the plan the model is working to needs this handle.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        Assert.IsNull(scope.Todo, "nothing is attached until asked for");

        scope.WithTodoTracking();

        Assert.IsNotNull(scope.Todo);
        CollectionAssert.Contains(scope.CreateContextProviders().ToArray(), scope.Todo,
            "the exposed instance must be the very one in the composition, not a second copy");
    }

    [TestMethod]
    public void WithTodoTracking_IsOptIn()
    {
        // A scope that was not asked for todos must not grow them: the composition is fixed, but what is
        // in it is not. A bare scope carries only its own provider.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        var providers = scope.CreateContextProviders();

        Assert.HasCount(1, providers);
        Assert.IsInstanceOfType<WorkflowAgentContextProvider>(providers[0]);
        Assert.IsFalse(providers.Any(p => p is TodoProvider));
    }

    // ── Modes ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task WithAgentModes_ReachesTheModelWithTheModeTools()
    {
        var options = new AgentModeProviderOptions
        {
            Modes =
            [
                new AgentModeProviderOptions.AgentMode("build", "You edit the graph."),
                new AgentModeProviderOptions.AgentMode("plan", "You only describe what you would change."),
            ],
            DefaultMode = "build",
        };

        var client = await OfflineAgent.RunOnce(new WorkflowAgentScope(new TreeDefaultViewModel()).WithAgentModes(options));

        CollectionAssert.IsSubsetOf(new[] { "mode_set", "mode_get" }, client.ToolNames);
        Assert.IsTrue(client.Prose.Any(p => p.Contains("build") && p.Contains("plan")),
            "every mode's instructions must reach the model, or the switch changes nothing");
    }

    [TestMethod]
    public void WithAgentModes_ExposesTheProviderForAHostToBind()
    {
        // A mode switch in the host UI needs both directions: read the current mode, and set it.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        Assert.IsNull(scope.AgentMode);

        scope.WithAgentModes(new AgentModeProviderOptions
        {
            Modes = [new AgentModeProviderOptions.AgentMode("build", "You edit the graph.")],
            DefaultMode = "build",
        });

        Assert.IsNotNull(scope.AgentMode);
    }

    [TestMethod]
    public void WithAgentModes_RefusesANullOptionsBag()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        Assert.ThrowsExactly<ArgumentNullException>(() => scope.WithAgentModes(null!));
    }

    // ── Compaction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void WithContextCompaction_PutsCompactionAheadOfEverythingElse()
    {
        // Compaction rewrites the message history, so every provider after it must see the bounded form.
        // Asserted through the state key rather than the type: the type lives in a namespace MAF marks
        // [Experimental], and a test has no business naming it any more than a host does.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithContextCompaction(maxContextWindowTokens: 128_000, maxOutputTokens: 8_000)
            .WithSkills(new SkillScope());

        var providers = scope.CreateContextProviders();

        Assert.HasCount(3, providers, "compaction, the scope's own, and the skill subsystem");
        Assert.IsTrue(providers[0].StateKeys[0].StartsWith("CompactionProvider:", StringComparison.Ordinal),
            "compaction must be first, not appended after the workflow slice");
        Assert.IsInstanceOfType<WorkflowAgentContextProvider>(providers[1]);
        Assert.IsInstanceOfType<SkillAgentContextProvider>(providers[2]);
    }

    [TestMethod]
    public void WithContextCompaction_KeysItsStatePerScope()
    {
        // Two trees each carrying their own compaction must not share one state key, which is what the
        // framework rejects at agent construction.
        var first = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithContextCompaction(128_000, 8_000).CreateContextProviders()[0];
        var second = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithContextCompaction(128_000, 8_000).CreateContextProviders()[0];

        Assert.AreNotEqual(first.StateKeys[0], second.StateKeys[0]);
    }

    // ── Composition ─────────────────────────────────────────────────────────

    [TestMethod]
    public void NativeCapabilities_SitAfterTheSubsystemsAndBeforeHostFactories()
    {
        // Fixed order, not attachment order — attached here in the reverse of what is asserted.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithContextProvider(_ => new MarkerProvider())
            .WithTodoTracking()
            .WithSkills(new SkillScope())
            .WithMcps(new McpScope());

        var providers = scope.CreateContextProviders();

        Assert.HasCount(5, providers);
        Assert.IsInstanceOfType<WorkflowAgentContextProvider>(providers[0]);
        Assert.IsInstanceOfType<SkillAgentContextProvider>(providers[1]);
        Assert.IsInstanceOfType<McpAgentContextProvider>(providers[2]);
        Assert.IsInstanceOfType<TodoProvider>(providers[3], "behavioural scaffolding follows the context sources");
        Assert.IsInstanceOfType<MarkerProvider>(providers[4], "a host's provider still goes last");
    }

    [TestMethod]
    public async Task EveryAttachedProvider_ContributesNoDuplicateToolName()
    {
        // The framework unions the providers' tool lists without deduplicating, so a name offered twice
        // reaches the model twice. Attaching everything at once is the arrangement that would expose it.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithTodoTracking()
            .WithAgentModes(new AgentModeProviderOptions
            {
                Modes = [new AgentModeProviderOptions.AgentMode("build", "You edit the graph.")],
                DefaultMode = "build",
            })
            .WithSkills(new SkillScope());

        var client = await OfflineAgent.RunOnce(scope);

        var duplicates = client.ToolNames
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.IsEmpty(duplicates,
            $"each tool must reach the model once; duplicated: {string.Join(", ", duplicates)}");
    }

    [TestMethod]
    public void AttachingANativeCapability_AdvancesTheVersion()
    {
        // Version is documented as "everything this scope can be configured with". Attaching a provider
        // changes what the Agent is shown, so a render cached against the old version is stale.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var before = scope.Version;

        scope.WithTodoTracking();
        Assert.IsGreaterThan(before, scope.Version);

        var afterTodo = scope.Version;
        scope.WithAgentModes(new AgentModeProviderOptions
        {
            Modes = [new AgentModeProviderOptions.AgentMode("build", "You edit the graph.")],
            DefaultMode = "build",
        });
        Assert.IsGreaterThan(afterTodo, scope.Version);

        var afterModes = scope.Version;
        scope.WithContextCompaction(128_000, 8_000);
        Assert.IsGreaterThan(afterModes, scope.Version);
    }

    /// <summary>A stand-in for a host-authored provider, so its position in the composition is visible.</summary>
    private sealed class MarkerProvider : AIContextProvider
    {
    }
}
