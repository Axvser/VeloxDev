using Microsoft.Extensions.AI;
using System.Linq;
using System.Threading;
using VeloxDev.AI.MCP;

namespace VeloxDev.Core.Extension.Test.Agent.MCP;

/// <summary>
/// Coverage for MCP's two distinct ways of taking a server out of play: <b>disable</b> (its tools stop
/// being offered, the connection stays up) and <b>unload</b> (the connection is torn down).
/// <para>
/// Everything here runs on <see cref="McpScope.SeedLoadedTools"/>, which fakes a connected server with no
/// transport. That covers every behaviour downstream of "this server's tools exist" — the contributed tool
/// list, the inventory block, the enable/disable semantics — but it is <b>not</b> transport coverage: real
/// connect/disconnect, the npm/pip install steps, OAuth and <c>ServerError</c> are exercised by
/// <c>McpRemoteTests</c> and a live server.
/// </para>
/// </summary>
[TestClass]
public class McpServerSwitchTests
{
    private static McpScope ScopeWithServer(params string[] toolNames)
    {
        var scope = new McpScope();
        scope.SeedLoadedTools("seeded", [.. toolNames.Select(n => AIFunctionFactory.Create(() => "ok", n))]);
        return scope;
    }

    [TestMethod]
    public void DisablingAServer_DropsItsTools_ButLeavesItConnected()
    {
        var scope = ScopeWithServer("tool_a", "tool_b");
        Assert.HasCount(2, scope.LoadedTools);

        Assert.IsTrue(scope.SetServerEnabled("seeded", false));

        Assert.IsEmpty(scope.LoadedTools, "a switched-off server contributes nothing");
        Assert.IsFalse(scope.IsServerEnabled("seeded"));
        Assert.AreEqual(McpServerStatus.Connected, scope.Status.Servers.Single().State,
            "disabling is not unloading — the connection stays up");
        Assert.AreEqual(2, scope.Status.Servers.Single().ToolCount,
            "the server still holds its tools; only the offering stopped");
    }

    [TestMethod]
    public void DisablingIsReversible_WithoutAReconnect()
    {
        var scope = ScopeWithServer("tool_a");

        scope.SetServerEnabled("seeded", false);
        scope.SetServerEnabled("seeded", true);

        Assert.HasCount(1, scope.LoadedTools);
        Assert.IsEmpty(scope.DisabledServerNames);
    }

    [TestMethod]
    public void DisablingOneServer_LeavesAnotherAlone()
    {
        var scope = ScopeWithServer("tool_a");
        scope.SeedLoadedTools("other", [AIFunctionFactory.Create(() => "ok", "tool_other")]);

        scope.SetServerEnabled("seeded", false);

        CollectionAssert.AreEqual(new[] { "tool_other" }, scope.LoadedTools.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void DisablingOneTool_LeavesTheServersOtherToolsExposed()
    {
        var scope = ScopeWithServer("tool_a", "tool_b");

        Assert.IsTrue(scope.SetToolEnabled("seeded", "tool_a", false));

        CollectionAssert.AreEqual(new[] { "tool_b" }, scope.LoadedTools.Select(t => t.Name).ToArray());
        Assert.IsTrue(scope.IsToolEnabled("seeded", "tool_b"));
    }

    [TestMethod]
    public void SameToolNameOnTwoServers_IsSwitchedIndependently()
    {
        var scope = ScopeWithServer("shared");
        scope.SeedLoadedTools("other", [AIFunctionFactory.Create(() => "ok", "shared")]);

        scope.SetToolEnabled("seeded", "shared", false);

        CollectionAssert.AreEqual(new[] { "shared" }, scope.LoadedTools.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void ASwitchMadeBeforeTheServerIsKnown_SurvivesItAppearing()
    {
        var scope = new McpScope();
        Assert.IsTrue(scope.SetServerEnabled("late", false));

        scope.SeedLoadedTools("late", [AIFunctionFactory.Create(() => "ok", "late_tool")]);

        Assert.IsFalse(scope.Status.Servers.Single().IsEnabled, "the row came back switched on");
        Assert.IsEmpty(scope.LoadedTools);
    }

    [TestMethod]
    public void TheInventoryBlock_ReportsASwitchedOffServerAsOfferingNothing()
    {
        var scope = ScopeWithServer("tool_a", "tool_b");
        Assert.Contains("| 2 |", scope.BuildInventoryBlock());

        scope.SetServerEnabled("seeded", false);
        var block = scope.BuildInventoryBlock();

        StringAssert.Contains(block, "disabled by host");
        StringAssert.Contains(block, "| 0 |");

        scope.SetServerEnabled("seeded", true);
        CollectionAssert.DoesNotContain(
            scope.BuildInventoryBlock().Split('\n').Select(l => l.Trim()).ToArray(), "| 0 |");
    }

    [TestMethod]
    public void DescribeMcpServer_RefusesASwitchedOffServer()
    {
        var scope = ScopeWithServer("tool_a");
        var describe = (AIFunction)new McpAgentToolkit(scope, []).CreateTools()
            .Single(t => t.Name == "DescribeMcpServer");

        scope.SetServerEnabled("seeded", false);

        var args = new AIFunctionArguments { ["serverName"] = "seeded" };
        var result = describe.InvokeAsync(args, CancellationToken.None).AsTask().GetAwaiter().GetResult()?.ToString() ?? "";
        StringAssert.Contains(result, "switched off by host policy");
    }

    [TestMethod]
    public void ListMcpServers_TellsTheModelAServerIsSwitchedOff()
    {
        var scope = ScopeWithServer("tool_a");
        var list = (AIFunction)new McpAgentToolkit(scope, []).CreateTools()
            .Single(t => t.Name == "ListMcpServers");

        scope.SetServerEnabled("seeded", false);

        var json = list.InvokeAsync(new AIFunctionArguments(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult()?.ToString() ?? string.Empty;

        StringAssert.Contains(json, "\"enabled\":false");
        StringAssert.Contains(json, "\"toolCount\":0", "the offered count must not contradict the tool set");
        StringAssert.Contains(json, "\"loadedToolCount\":1");
    }

    [TestMethod]
    public void UnloadingAServer_ResetsTheReportedToolCount()
    {
        var scope = ScopeWithServer("tool_a", "tool_b");

        scope.UnloadServer("seeded");

        var row = scope.Status.Servers.Single();
        Assert.AreEqual(McpServerStatus.NotStarted, row.State);
        Assert.AreEqual(0, row.ToolCount, "the row must not keep advertising tools that went with the client");
        Assert.IsEmpty(scope.GetServerTools("seeded"));
        Assert.IsEmpty(scope.LoadedTools);
    }

    [TestMethod]
    public void EverySwitchAdvancesTheVersion_SoTheProviderReRenders()
    {
        var scope = ScopeWithServer("tool_a");
        var before = scope.Version;

        scope.SetServerEnabled("seeded", false);
        Assert.IsGreaterThan(before, scope.Version);

        var mid = scope.Version;
        scope.SetToolEnabled("seeded", "tool_a", false);
        Assert.IsGreaterThan(mid, scope.Version);

        var last = scope.Version;
        Assert.IsFalse(scope.SetServerEnabled("seeded", false), "already off");
        Assert.AreEqual(last, scope.Version, "a no-op switch must not churn the provider cache");
    }

    [TestMethod]
    public void AnUnknownServer_IsHarmless()
    {
        var scope = new McpScope();

        Assert.IsTrue(scope.SetServerEnabled("never-heard-of-it", false), "the switch is remembered");
        Assert.IsEmpty(scope.LoadedTools);
        Assert.IsFalse(scope.SetServerEnabled("", false));
        Assert.IsFalse(scope.IsServerEnabled("never-heard-of-it"), "the remembered switch is what makes it off");
    }
}
