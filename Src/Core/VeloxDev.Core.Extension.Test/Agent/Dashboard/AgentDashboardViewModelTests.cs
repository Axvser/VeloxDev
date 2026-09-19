using Microsoft.Extensions.AI;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Dashboard;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Dashboard;

/// <summary>
/// Coverage for the Tools / MCP / Skills dashboard: that each row reflects the scope, that a checkbox
/// actually changes what the model is offered (not just what the panel draws), and that the two directions
/// of the switch converge instead of echoing.
/// </summary>
[TestClass]
public class AgentDashboardViewModelTests
{
    /// <summary>Invokes a tool instance the way an agent host does.</summary>
    private static string InvokeCaptured(AIFunction tool)
    {
        var result = tool.InvokeAsync(new AIFunctionArguments(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result switch
        {
            string s => s,
            JsonElement je => je.GetString() ?? string.Empty,
            _ => result?.ToString() ?? string.Empty,
        };
    }

    /// <summary>Invokes a registered workflow tool by name.</summary>
    private static string InvokeTool(WorkflowAgentScope scope, string toolName)
        => InvokeCaptured((AIFunction)(scope.ProvideTools().FirstOrDefault(t => t.Name == toolName)
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not registered.")));

    private static SkillScope EmbeddedSkills()
    {
        var skills = new SkillScope().WithSource(new EmbeddedSkillSource("Workflow"));
        skills.Refresh();
        return skills;
    }

    private static WorkflowAgentScope ScopeWithSkills() => new WorkflowAgentScope(new TreeDefaultViewModel())
        .WithSkills(EmbeddedSkills());

    /// <summary>A server with tools but no transport — the seam that covers everything downstream of a connect.</summary>
    private static McpScope McpWithSeededServer(string serverName = "seeded", params string[] toolNames)
    {
        var mcp = new McpScope();
        mcp.SeedLoadedTools(serverName, [.. toolNames.Select(n => AIFunctionFactory.Create(() => "ok", n))]);
        return mcp;
    }

    // ── Inventory ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Create_ListsEveryWorkflowTool_AndTheFourSkillTools()
    {
        var scope = ScopeWithSkills();
        using var panel = AgentDashboardViewModel.Create(scope);

        CollectionAssert.AreEquivalent(
            scope.ProvideTools().Select(t => t.Name).ToArray(),
            panel.SystemTools.Select(t => t.Name).ToArray(),
            "the panel must list exactly what the scope would offer");

        CollectionAssert.AreEquivalent(
            SkillAgentToolkit.ToolNames,
            panel.SkillTools.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void Create_WithoutSkills_LeavesTheSkillToolGroupEmpty()
    {
        using var panel = AgentDashboardViewModel.Create(new WorkflowAgentScope(new TreeDefaultViewModel()));

        Assert.IsEmpty(panel.SkillTools);
        Assert.IsEmpty(panel.Skills);
        Assert.IsNotEmpty(panel.SystemTools);
    }

    [TestMethod]
    public void SystemTools_ReportHostGating_SoAGreenRowIsNotALie()
    {
        // Node execution is off by default and the tools are still registered; they just refuse. The row
        // has to say so, or the panel shows a usable-looking tool that can never work.
        using var panel = AgentDashboardViewModel.Create(new WorkflowAgentScope(new TreeDefaultViewModel()));

        var execute = panel.SystemTools.Single(t => t.Name == "ExecuteNode");
        Assert.IsTrue(execute.IsEnabled, "the tool is offered; it is the policy behind it that refuses");
        Assert.IsFalse(execute.IsReachable);
        Assert.Contains("WithAllowNodeExecution", execute.Note);

        var listNodes = panel.SystemTools.Single(t => t.Name == "ListNodes");
        Assert.IsTrue(listNodes.IsReachable);
        Assert.AreEqual(string.Empty, listNodes.Note);
    }

    // ── Tool switches ───────────────────────────────────────────────────────

    [TestMethod]
    public void UncheckingATool_TakesItOutOfTheTurnToolSet_AndRestores()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        using var panel = AgentDashboardViewModel.Create(scope);

        var row = panel.SystemTools.Single(t => t.Name == "ListNodes");
        row.IsEnabled = false;

        Assert.IsFalse(scope.IsToolEnabled("ListNodes"), "the checkbox must reach the scope");
        CollectionAssert.DoesNotContain(scope.ProvideTools().Select(t => t.Name).ToArray(), "ListNodes");
        CollectionAssert.DoesNotContain(
            scope.ProvideTools(WorkflowToolCategory.Query).Select(t => t.Name).ToArray(), "ListNodes");

        // Offered is not the same as callable: the shared policy refuses it if the model asks anyway.
        CollectionAssert.Contains(scope.ProvideTools().Select(t => t.Name).ToArray(), "GetWorkflowSummary");

        row.IsEnabled = true;
        CollectionAssert.Contains(scope.ProvideTools().Select(t => t.Name).ToArray(), "ListNodes");
    }

    [TestMethod]
    public void UncheckingATool_ReRendersTheContextProvidersToolList()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var provider = new WorkflowAgentContextProvider(scope);

        var before = provider.BuildContext().Tools;
        CollectionAssert.Contains(before!.Select(t => t.Name).ToArray(), "ListNodes");

        using var panel = AgentDashboardViewModel.Create(scope);
        panel.SystemTools.Single(t => t.Name == "ListNodes").IsEnabled = false;

        var after = provider.BuildContext().Tools;
        Assert.AreNotSame(before, after, "the cached render must be invalidated by the switch");
        CollectionAssert.DoesNotContain(after!.Select(t => t.Name).ToArray(), "ListNodes");
    }

    [TestMethod]
    public void UncheckingASkillTool_ReachesTheSharedToolPolicy()
    {
        // The skill tools are contributed by the skill provider, not the workflow toolkit, so the switch can
        // only reach them through the policy every slice shares. A filter on the workflow tool list alone
        // would leave this silently doing nothing — the row would draw as switched off and still work.
        var scope = ScopeWithSkills();
        using var panel = AgentDashboardViewModel.Create(scope);

        // Captured before the switch: the refusal has to be enforced at call time, not by the tool merely
        // disappearing from a list someone else builds.
        var tool = (AIFunction)((SkillAgentContextProvider)scope.Skills!
            .CreateContextProvider(scope.CreateToolkit().Tools, scope.Pipeline))
            .BuildContext().Tools!.Single(t => t.Name == "ListSkills");

        panel.SkillTools.Single(t => t.Name == "ListSkills").IsEnabled = false;

        Assert.IsFalse(scope.IsToolEnabled("ListSkills"));
        StringAssert.Contains(InvokeCaptured(tool), "disabled by host policy");
    }

    [TestMethod]
    public void ARefusedTool_IsReportedToTheModel_AndDoesNotCountAsACall()
    {
        // A refused call never reaches AfterCall, so it must not inflate the activity column either.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMaxToolCalls(10);
        using var panel = AgentDashboardViewModel.Create(scope);

        var tool = (AIFunction)scope.ProvideTools().Single(t => t.Name == "ListNodes");
        panel.SystemTools.Single(t => t.Name == "ListNodes").IsEnabled = false;

        StringAssert.Contains(InvokeCaptured(tool), "disabled by host policy");
        Assert.AreEqual(0, panel.SystemTools.Single(t => t.Name == "ListNodes").CallCount);
    }

    // ── Activity ────────────────────────────────────────────────────────────

    [TestMethod]
    public void CallCounts_TrackEachToolIndependently_AcrossEverySlice()
    {
        var scope = ScopeWithSkills();
        using var panel = AgentDashboardViewModel.Create(scope);

        InvokeTool(scope, "ListNodes");
        InvokeTool(scope, "ListNodes");
        InvokeTool(scope, "GetWorkflowSummary");
        // Contributed by the skill provider rather than the workflow toolkit, so it is reached through the
        // composed provider — the same route the model's call takes.
        InvokeCaptured((AIFunction)((SkillAgentContextProvider)scope.Skills!
            .CreateContextProvider(scope.CreateToolkit().Tools, scope.Pipeline))
            .BuildContext().Tools!.Single(t => t.Name == "ListSkills"));

        Assert.AreEqual(2, panel.SystemTools.Single(t => t.Name == "ListNodes").CallCount);
        Assert.AreEqual(1, panel.SystemTools.Single(t => t.Name == "GetWorkflowSummary").CallCount);
        Assert.AreEqual(1, panel.SkillTools.Single(t => t.Name == "ListSkills").CallCount);

        Assert.IsTrue(panel.SystemTools.Single(t => t.Name == "ListNodes").HasBeenCalled);
        Assert.IsFalse(panel.SystemTools.Single(t => t.Name == "CreateNode").HasBeenCalled);
        Assert.AreEqual("未调用", panel.SystemTools.Single(t => t.Name == "CreateNode").ActivityText);
        Assert.AreEqual("2 次", panel.SystemTools.Single(t => t.Name == "ListNodes").ActivityText);
    }

    [TestMethod]
    public void CallCount_AlsoLandsOnAnMcpServerTool()
    {
        var mcp = McpWithSeededServer("seeded", "server_tool");
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        var server = panel.McpServers.Single();
        var subTool = server.Tools.Single();

        // Invoke it through the MCP provider's own tool list, which is what the model would call.
        var tool = new McpAgentContextProvider(mcp, scope.CreateToolkit().Tools, scope.Pipeline)
            .BuildContext().Tools!.Single(t => t.Name == "server_tool");
        ((AIFunction)tool).InvokeAsync(new AIFunctionArguments(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(1, server.Tools.Single(t => t.Name == subTool.Name).CallCount);
    }

    // ── Skills ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void UncheckingASkill_DisablesItInTheScope_AndDropsItsTextFromThePrompt()
    {
        var skills = EmbeddedSkills();
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSkills(skills);
        using var panel = AgentDashboardViewModel.Create(scope);

        Assert.Contains("Smart Layout", skills.BuildEmbeddedBlock(AgentLanguages.English));

        panel.Skills.Single(s => s.Name == "smart-layout").IsEnabled = false;

        Assert.IsFalse(skills.Find("smart-layout")!.IsEnabled, "the switch must go through the scope");
        Assert.DoesNotContain("Smart Layout", skills.BuildEmbeddedBlock(AgentLanguages.English));
    }

    [TestMethod]
    public void ASkillDisabledInTheScope_FlowsBackToTheCheckbox()
    {
        var skills = EmbeddedSkills();
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSkills(skills);
        using var panel = AgentDashboardViewModel.Create(scope);

        Assert.IsTrue(panel.Skills.Single(s => s.Name == "smart-layout").IsEnabled);

        // The Agent's own UnloadSkill tool takes this path.
        skills.Disable("smart-layout");

        Assert.IsFalse(panel.Skills.Single(s => s.Name == "smart-layout").IsEnabled,
            "a switch made behind the panel's back must still reach it");
    }

    [TestMethod]
    public void ASkillAddedByARefresh_AppearsInThePanel()
    {
        var skills = EmbeddedSkills();
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSkills(skills);
        using var panel = AgentDashboardViewModel.Create(scope);
        var before = panel.Skills.Count;

        // Refresh() goes through Status.Reset(), which is the case a plain Add/Remove subscription misses.
        skills.Refresh();

        Assert.HasCount(before, panel.Skills);
        Assert.IsTrue(panel.Skills.All(s => s.IsEnabled));
    }

    // ── MCP ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void McpAggregates_SplitAliveFromFailed_AndAppearInTheSummary()
    {
        // The standalone MCP panel used to own these counts; the merged panel has to keep reporting them.
        var mcp = McpWithSeededServer("seeded", "tool_a");
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        Assert.AreEqual("存活 1 · 错误 0", panel.McpStatusText);
        StringAssert.Contains(panel.SummaryText, "存活 1 · 错误 0");
        Assert.IsTrue(panel.CanReloadMcp);

        // A failed state is exclusive of connected, so the server moves buckets rather than doubling up.
        mcp.Status.Servers.Single().State = McpServerStatus.Error;
        Assert.AreEqual("存活 0 · 错误 1", panel.McpStatusText);
    }

    [TestMethod]
    public async Task ReloadAllServersAsync_WithNothingRegistered_IsANoOp()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(new McpScope());
        using var panel = AgentDashboardViewModel.Create(scope);

        await panel.ReloadAllServersAsync();

        Assert.IsEmpty(panel.McpServers);
        Assert.AreEqual("存活 0 · 错误 0", panel.McpStatusText);
    }

    [TestMethod]
    public async Task ReloadAllServersAsync_WithoutAnMcpScope_IsANoOp()
    {
        using var panel = AgentDashboardViewModel.Create(new WorkflowAgentScope(new TreeDefaultViewModel()));

        Assert.IsFalse(panel.CanReloadMcp);
        await panel.ReloadAllServersAsync();   // must not throw when nothing is attached
    }

    [TestMethod]
    public void McpServerRow_StartsWithItsToolsVisible_AndToggles()
    {
        var mcp = McpWithSeededServer("seeded", "tool_a");
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        var server = panel.McpServers.Single();
        Assert.IsTrue(server.IsExpanded, "a freshly built row shows its tools");
        Assert.AreEqual("▾", server.ExpandGlyph);

        server.ToggleExpand();
        Assert.IsFalse(server.IsExpanded);
        Assert.AreEqual("▸", server.ExpandGlyph);

        // A rebuild must not silently re-open or close what the host chose.
        panel.Rebuild();
        Assert.IsFalse(panel.McpServers.Single().IsExpanded);
    }

    [TestMethod]
    public void McpServerRow_ExposesItsSubTools()
    {
        var mcp = McpWithSeededServer("seeded", "server_tool_a", "server_tool_b");
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        var server = panel.McpServers.Single();
        Assert.AreEqual("seeded", server.Name);
        Assert.IsTrue(server.IsConnected);
        Assert.AreEqual(2, server.LoadedToolCount);
        CollectionAssert.AreEquivalent(
            mcp.GetServerTools("seeded").Select(t => t.Name).ToArray(),
            server.Tools.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void DisablingAServer_KeepsItConnected_ButDropsItsTools()
    {
        var mcp = McpWithSeededServer("seeded", "server_tool");
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        var server = panel.McpServers.Single();
        server.IsEnabled = false;

        Assert.IsFalse(mcp.IsServerEnabled("seeded"), "the checkbox must reach the scope");
        Assert.IsTrue(server.IsConnected, "disabling is not unloading — the connection stays up");
        Assert.IsEmpty(mcp.LoadedTools, "a switched-off server contributes nothing");
        Assert.IsEmpty(
            new McpAgentContextProvider(mcp, scope.CreateToolkit().Tools, scope.Pipeline).BuildContext().Tools!
                .Where(t => t.Name.StartsWith("server_tool", StringComparison.Ordinal)).ToArray());
        Assert.Contains("disabled by host", mcp.BuildInventoryBlock());

        server.IsEnabled = true;
        Assert.HasCount(1, mcp.LoadedTools, "switching back on restores the tools with no reconnect");
    }

    [TestMethod]
    public void DisablingOneServerTool_LeavesTheOtherExposed()
    {
        var mcp = McpWithSeededServer("seeded", "tool_a", "tool_b");
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        var server = panel.McpServers.Single();
        server.Tools.Single(t => t.Name == "tool_a").IsEnabled = false;

        CollectionAssert.AreEqual(new[] { "tool_b" }, mcp.LoadedTools.Select(t => t.Name).ToArray());
        Assert.IsTrue(mcp.IsServerEnabled("seeded"));
    }

    [TestMethod]
    public async Task UnloadingAServer_EmptiesItsToolsAndReportsZero()
    {
        var mcp = McpWithSeededServer("seeded", "server_tool");
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        await panel.McpServers.Single().UnloadAsync();

        var server = panel.McpServers.Single();
        Assert.IsFalse(server.IsConnected);
        Assert.IsEmpty(server.Tools);
        Assert.AreEqual(0, server.LoadedToolCount);
        Assert.AreEqual(0, mcp.Status.Servers.Single().ToolCount, "the row must not keep advertising old tools");
    }

    [TestMethod]
    public void AServerDisabledBeforeItLoads_StaysDisabledOnceItAppears()
    {
        var mcp = new McpScope();
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMcps(mcp);
        using var panel = AgentDashboardViewModel.Create(scope);

        // No row exists yet — the switch has to be remembered anyway.
        Assert.IsTrue(mcp.SetServerEnabled("late", false));

        mcp.SeedLoadedTools("late", [AIFunctionFactory.Create(() => "ok", "late_tool")]);

        Assert.IsFalse(panel.McpServers.Single().IsEnabled, "the row came back switched on");
        Assert.IsEmpty(mcp.LoadedTools);
    }

    // ── Convergence ─────────────────────────────────────────────────────────

    [TestMethod]
    public void SwitchingFromThePanel_PushesOnceAndDoesNotEchoBack()
    {
        var skills = EmbeddedSkills();
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSkills(skills);
        using var panel = AgentDashboardViewModel.Create(scope);

        var row = panel.Skills.Single(s => s.Name == "smart-layout");
        var writes = 0;
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(row.IsEnabled)) writes++; };

        row.IsEnabled = false;

        Assert.AreEqual(1, writes, "one host write must produce exactly one notification");
        Assert.IsFalse(row.IsEnabled);
        Assert.IsFalse(skills.Find("smart-layout")!.IsEnabled);
    }

    [TestMethod]
    public void SwitchingAnAlreadyMatchingValue_IsANoOp()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        using var panel = AgentDashboardViewModel.Create(scope);

        var row = panel.SystemTools.Single(t => t.Name == "ListNodes");
        Assert.IsTrue(row.IsEnabled);

        row.IsEnabled = true;   // same value: the generated setter short-circuits before the hook

        Assert.IsTrue(scope.IsToolEnabled("ListNodes"));
        Assert.IsEmpty(scope.DisabledToolNames);
    }

    // ── Threading ───────────────────────────────────────────────────────────

    /// <summary>A context that owns one thread and installs itself there, like a dispatcher.</summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = [];

        public int ThreadId { get; private set; }

        public SingleThreadContext()
        {
            var thread = new Thread(() =>
            {
                ThreadId = Environment.CurrentManagedThreadId;
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
            })
            { IsBackground = true, Name = "dashboard-context" };
            thread.Start();
            SpinWait.SpinUntil(() => ThreadId != 0, TimeSpan.FromSeconds(5));
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
        public void Dispose() => _queue.CompleteAdding();
    }

    [TestMethod]
    public void ScopeChangesFromAnotherThread_ReachThePanelOnTheBoundThread()
    {
        using var context = new SingleThreadContext();
        var skills = EmbeddedSkills();
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithSkills(skills);

        AgentDashboardViewModel panel = null!;
        var built = new ManualResetEventSlim();
        var offThreadMutations = 0;

        context.Post(_ =>
        {
            panel = AgentDashboardViewModel.Create(scope);
            panel.Skills.CollectionChanged += (_, _) =>
            {
                if (Environment.CurrentManagedThreadId != context.ThreadId)
                    Interlocked.Increment(ref offThreadMutations);
            };
            built.Set();
        }, null);
        Assert.IsTrue(built.Wait(TimeSpan.FromSeconds(5)), "the panel never came up on the bound thread");

        // Drive the scope from this thread — a background load does exactly this.
        skills.Disable("smart-layout");

        var settled = SpinWait.SpinUntil(
            () =>
            {
                var off = false;
                var read = new ManualResetEventSlim();
                context.Post(_ => { off = panel.Skills.Single(s => s.Name == "smart-layout").IsEnabled == false; read.Set(); }, null);
                read.Wait(TimeSpan.FromSeconds(1));
                return off;
            },
            TimeSpan.FromSeconds(5));

        Assert.IsTrue(settled, "the switch made off-thread never reached the panel");
        Assert.AreEqual(0, offThreadMutations, "the panel's bound collection was mutated from the wrong thread");

        context.Post(_ => panel.Dispose(), null);
    }
}
