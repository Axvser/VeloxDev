using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// What the workflow layer looks like once it composes the two subsystems rather than reimplementing them.
/// <para>
/// These are the tests that would catch a composition regression — the slices are each correct on their
/// own and only their combination can go wrong.
/// </para>
/// </summary>
[TestClass]
public class ComposedProvidersTests
{
    /// <summary>A scope with both subsystems attached and the skill corpus actually discovered.</summary>
    private static WorkflowAgentScope Composed()
        => new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithSkills(DiscoveredSkills())
            .WithMcps(new McpScope());

    private static SkillScope DiscoveredSkills()
    {
        var skills = new SkillScope().WithSource(new EmbeddedSkillSource("Workflow"));
        skills.Refresh();
        return skills;
    }

    private static IReadOnlyList<AITool> AllTools(WorkflowAgentScope scope)
        => [.. scope.CreateContextProviders().SelectMany(p => Build(p).Tools ?? [])];

    private static string? AllInstructions(WorkflowAgentScope scope)
        => string.Join("\n", scope.CreateContextProviders()
            .Select(p => Build(p).Instructions)
            .Where(i => !string.IsNullOrEmpty(i)));

    /// <summary>Each provider renders through the same internal entry point the framework uses.</summary>
    private static AIContext Build(AIContextProvider provider) => provider switch
    {
        WorkflowAgentContextProvider w => w.BuildContext(),
        SkillAgentContextProvider s => s.BuildContext(),
        McpAgentContextProvider m => m.BuildContext(),
        _ => throw new InvalidOperationException($"Unhandled provider {provider.GetType().Name}"),
    };

    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null)
                callArgs[name] = value;

        var result = ((AIFunction)tool)
            .InvokeAsync(callArgs, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>A connected server's tools, without a transport.</summary>
    private static AITool ServerTool(string name)
        => AIFunctionFactory.Create(() => "ok", name);

    [TestMethod]
    public void ComposedProviders_ContributeNoDuplicateToolName()
    {
        // The framework unions tool lists with Enumerable.Concat and does NOT deduplicate by name, so a
        // tool contributed by two providers reaches the model twice.
        //
        // A server is seeded on purpose: without one, the loaded-server path contributes nothing and this
        // test would pass even if the workflow slice went back to wrapping Mcp.LoadedTools itself — which
        // is the exact regression it exists to catch.
        var mcp = new McpScope();
        mcp.SeedLoadedTools("seeded", [ServerTool("server_tool_a"), ServerTool("server_tool_b")]);

        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithSkills(DiscoveredSkills())
            .WithMcps(mcp);

        var tools = AllTools(scope);

        CollectionAssert.Contains(tools.Select(t => t.Name).ToArray(), "server_tool_a",
            "the loaded server's tools must be offered exactly once, by the MCP slice");
        var duplicates = tools.GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.IsEmpty(duplicates, $"tool names contributed twice: {string.Join(", ", duplicates)}");
    }

    [TestMethod]
    public void WorkflowSlice_DoesNotWrapLoadedServerTools()
    {
        // The narrow form of the test above: the regression is specifically the workflow slice reaching
        // into MCP, so assert directly that it does not.
        var mcp = new McpScope();
        mcp.SeedLoadedTools("seeded", [ServerTool("server_tool_a")]);

        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        var workflowNames = Build(scope.CreateContextProvider()).Tools!.Select(t => t.Name).ToArray();

        CollectionAssert.DoesNotContain(workflowNames, "server_tool_a");
        CollectionAssert.Contains(AllTools(scope).Select(t => t.Name).ToArray(), "server_tool_a");
    }

    [TestMethod]
    public void ComposedProviders_ContributeEverySlice()
    {
        // A guard on the test above: three empty slices would also have no duplicates.
        var names = AllTools(Composed()).Select(t => t.Name).ToArray();

        CollectionAssert.Contains(names, "ListNodes");                      // workflow
        CollectionAssert.Contains(names, SkillAgentToolkit.ToolNames[0]);   // skills
        CollectionAssert.Contains(names, McpAgentToolkit.ToolNames[0]);     // mcp
    }

    [TestMethod]
    public void ComposedProviders_ShareOnePolicy_SoBudgetsApplyAcrossSlices()
    {
        // The tension the design had to resolve: standalone subsystems are "thread only", but composed
        // into the workflow agent their tools must still count. This fails if a subsystem provider is
        // handed a policy of its own instead of the scope's.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithSkills(DiscoveredSkills())
            .WithMaxToolCalls(1);

        var tools = AllTools(scope);
        var workflowTool = tools.First(t => t.Name == "GetWorkflowSummary");
        var skillTool = tools.First(t => t.Name == SkillAgentToolkit.ToolNames[0]);

        // GetWorkflowSummary carries no `status` field, so its success is recognised by its own payload.
        Assert.IsNotNull(JObject.Parse(Invoke(workflowTool))["treeId"], "the first call must go through");

        var refused = JObject.Parse(Invoke(skillTool));
        Assert.AreEqual("error", refused["status"]?.Value<string>(),
            "the skill tool must share the workflow scope's call budget");
        Assert.Contains("limit", refused["message"]?.Value<string>() ?? string.Empty);
    }

    [TestMethod]
    public void ComposedProviders_RaiseOneCallNotificationAcrossSlices()
    {
        var scope = Composed();
        var seen = new List<AgentToolCallEventArgs>();
        scope.ToolCalled += (_, e) => seen.Add(e);

        var tools = AllTools(scope);
        Invoke(tools.First(t => t.Name == "GetWorkflowSummary"));
        Invoke(tools.First(t => t.Name == SkillAgentToolkit.ToolNames[0]));

        Assert.HasCount(2, seen, "a skill tool must be reported like any other");
        Assert.AreEqual(1, seen[0].CallCount);
        Assert.AreEqual(2, seen[1].CallCount, "one counter across every slice, not one per provider");
    }

    [TestMethod]
    public void SkillTools_AreClassifiedAsQueries_WhereverTheyComeFrom()
    {
        // The skill tools change what the model is shown, never the graph. They must therefore be charged
        // to the query budget and never the mutation one — this held while the host registered them via
        // WithQueryTools, and has to keep holding now that the provider contributes them instead.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithSkills(DiscoveredSkills())
            .WithMaxWriteToolCalls(0)      // no mutation budget at all
            .WithMaxReadToolCalls(10);

        var skillTool = AllTools(scope).First(t => t.Name == SkillAgentToolkit.ToolNames[0]);
        var json = JObject.Parse(Invoke(skillTool));

        Assert.AreNotEqual("error", json["status"]?.Value<string>(),
            $"a query tool must not be charged to the mutation budget: {json}");
    }

    [TestMethod]
    public void ComposedProviders_ConcatenatePromptTextInAFixedOrder()
    {
        // Both subsystem providers contribute instructions and the framework concatenates them in provider
        // order, so the order is the prompt. Fixed rather than attachment order — this scope is built
        // skills-then-mcp, and the assertion is that it reads skills-then-mcp either way.
        var text = AllInstructions(Composed());

        Assert.IsNotNull(text);
        var skillAt = text.IndexOf("Skill:", StringComparison.Ordinal);
        var mcpAt = text.IndexOf("MCP server management tools", StringComparison.Ordinal);

        Assert.IsTrue(skillAt >= 0, "the skill corpus must be contributed");
        Assert.IsTrue(mcpAt >= 0, "the MCP description must be contributed");
        Assert.IsTrue(skillAt < mcpAt, "skills are contributed before MCP regardless of attachment order");
    }

    [TestMethod]
    public void WorkflowSlice_OwnsNoSubsystemTool()
    {
        // The complement of the no-duplicates test: the workflow provider must not speak for a subsystem.
        var scope = Composed();
        var workflowNames = Build(scope.CreateContextProvider()).Tools!.Select(t => t.Name).ToArray();

        foreach (var name in SkillAgentToolkit.ToolNames)
            CollectionAssert.DoesNotContain(workflowNames, name);
        foreach (var name in McpAgentToolkit.ToolNames)
            CollectionAssert.DoesNotContain(workflowNames, name);
    }
}
