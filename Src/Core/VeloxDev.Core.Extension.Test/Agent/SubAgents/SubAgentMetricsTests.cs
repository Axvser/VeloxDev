using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.SubAgents;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The three things a panel needs to render a row the way an agent map does: how long the child ran, what it
/// cost, and what the node above it cost in total.
/// </summary>
[TestClass]
public class SubAgentMetricsTests
{
    [TestMethod]
    public async Task TokenUsage_FromTheProvider_ReachesTheRowAndTheSummary()
    {
        // The load-bearing assumption of the whole feature: the usage a chat client reports survives the
        // agent wrap and arrives on AgentResponse.Usage. If a future MAF stops aggregating it, this is the
        // test that says so rather than a panel that silently shows nothing.
        await using var fx = new SubAgentFixture(client: new UsageChatClient(inputTokens: 120, outputTokens: 45));

        var id = fx.Spawn("the job");
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        var row = fx.RowVm(id);
        Assert.AreEqual(165L, row.TokensUsed, "the provider's total survives the wrap");
        Assert.AreEqual(120L, row.InputTokens);
        Assert.AreEqual(45L, row.OutputTokens);
        Assert.IsTrue(row.HasTokens);
        Assert.AreEqual("165", row.TokensText, "below a thousand, the exact figure is the readable one");

        Assert.AreEqual(165L, fx.RowOf(id).TokensUsed, "and the off-thread copy carries it too");
    }

    [TestMethod]
    public async Task AProviderThatReportsNothing_IsNotAZero()
    {
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        var id = fx.Spawn("the job");
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        var row = fx.RowVm(id);
        Assert.IsNull(row.TokensUsed, "not measured is not the same as nothing spent");
        Assert.IsFalse(row.HasTokens);
        Assert.AreEqual(string.Empty, row.TokensText);
        Assert.IsFalse(fx.RowOf(id).HasTokens);
    }

    [TestMethod]
    public async Task Duration_CoversTheRunAndReadsAsText()
    {
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        var id = fx.Spawn("the job");
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        var row = fx.RowVm(id);
        Assert.IsNotNull(row.StartedAt);
        Assert.IsNotNull(row.FinishedAt);
        Assert.IsNotNull(row.Duration, "a finished child has a span");
        Assert.IsTrue(row.Duration!.Value >= TimeSpan.Zero);
        Assert.AreEqual("0秒", row.DurationText, "an instant child is still a row with a clock on it");
    }

    [TestMethod]
    public async Task ATick_MovesTheClockWithoutTheRosterHavingToChange()
    {
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);

        var id = fx.Spawn("the job");
        gate.WaitForCalls(1);

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);
        var node = tree.Roots.Single();

        Assert.IsTrue(node.HasDuration, "a running child already has a span");

        var announced = new List<string>();
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SubAgentTreeNodeViewModel.DurationText)) announced.Add(node.DurationText);
        };

        var before = node.Duration!.Value;
        Thread.Sleep(30);
        tree.TickElapsed();
        var after = node.Duration!.Value;

        Assert.IsTrue(after > before, "time passed");
        Assert.IsNotEmpty(announced, "and the tick is what tells a bound panel about it");
    }

    [TestMethod]
    public async Task SubtreeTotals_SumBottomUpAndSitOnTheScopeRoot()
    {
        await using var fx = new SubAgentFixture(client: new UsageChatClient(inputTokens: 100, outputTokens: 0));

        var parent = fx.Spawn("the outer job", ("name", "alpha"));
        var childScope = fx.DescendantSubAgents(parent);
        var grand = SpawnGrandchild(fx, parent, "the nested job");

        // The grandchild's row lives in the child scope's roster, not the root scope's — so the wait has to
        // read the scope that issued the handle.
        SubAgentFixture.WaitFor(() =>
            fx.RowOf(parent).State == SubAgentState.Completed
            && childScope.Snapshot.Any(s => s.Id == grand && s.State == SubAgentState.Completed));

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);
        var node = tree.Roots.Single();

        Assert.AreEqual(100L, node.TokensUsed, "the row reports its own spend");
        Assert.AreEqual(200L, node.SubtreeTokens, "and the node reports its child's on top");
        Assert.IsTrue(node.ShowSubtreeTokens, "so both figures are worth printing");
        Assert.AreEqual("200", node.SubtreeTokensText);
        Assert.AreEqual(200L, tree.SubtreeTokens);

        Assert.IsNull(tree.ScopeRoot.TokensUsed, "the library cannot measure the host's own agent");
        Assert.AreEqual("200", tree.ScopeRoot.TokensText, "so the top falls back to the subtree total");
    }

    [TestMethod]
    public async Task AScopeThatKnowsItsOwnSpend_ShowsItAndTheSubtreeSeparately()
    {
        await using var fx = new SubAgentFixture(client: new UsageChatClient(inputTokens: 100, outputTokens: 0));

        var id = fx.Spawn("the job");
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);

        tree.ScopeRoot.ScopeTokens = 5000;
        tree.ScopeRoot.ScopeTitle = "主代理";

        Assert.AreEqual("主代理", tree.ScopeRoot.Title);
        Assert.AreEqual(5000L, tree.ScopeRoot.TokensUsed);
        Assert.AreEqual(5100L, tree.ScopeRoot.SubtreeTokens, "the subtree total is the scope's plus everything under it");
        Assert.AreEqual("5k", tree.ScopeRoot.TokensText, "the row shows the scope's own figure");
        Assert.IsTrue(tree.ScopeRoot.ShowSubtreeTokens);
    }

    [TestMethod]
    public async Task AnEmptyTree_StillHasTheScopeAsItsSingleTopNode()
    {
        await using var fx = new SubAgentFixture();
        using var tree = new SubAgentTreeViewModel(fx.SubAgents);

        Assert.HasCount(1, tree.Tree);
        Assert.AreSame(tree.ScopeRoot, tree.Tree[0]);
        Assert.IsTrue(tree.ScopeRoot.IsScopeRoot);
        Assert.IsNull(tree.ScopeRoot.Row);
        Assert.IsNull(tree.ScopeRoot.Parent);
        Assert.AreEqual(0, tree.ScopeRoot.Depth);
        Assert.AreEqual(0, tree.TotalCount, "the scope is not one of its own sub-agents");
        Assert.IsTrue(tree.IsEmpty);
        Assert.IsFalse(tree.ScopeRoot.HasTokens);
    }

    [TestMethod]
    public async Task ARebuild_ReconcilesTheLevelInPlace()
    {
        // The reason this is a test and not an implementation detail: the roster republishes on every
        // property write of every row, so a rebuild that cleared its level would reset the panel's containers
        // several times per child. A reset would also throw away expansion and selection along with them.
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);

        fx.Spawn("the first job", ("name", "alpha"));
        fx.Spawn("the second job", ("name", "beta"));
        gate.WaitForCalls(2);

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);
        var before = tree.Roots.ToArray();
        Assert.HasCount(2, before);

        var resets = 0;
        var added = 0;
        tree.Roots.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            if (e.Action == NotifyCollectionChangedAction.Add) added++;
        };

        // A sibling finishing republishes the roster, and a third arriving changes the level's contents.
        gate.ReleaseAll();
        SubAgentFixture.WaitFor(() => tree.IsIdle);
        fx.Spawn("the third job", ("name", "gamma"));
        SubAgentFixture.WaitFor(() => tree.Roots.Count == 3);

        Assert.AreEqual(0, resets, "a rebuild reconciles in place instead of clearing the level");
        Assert.AreEqual(1, added, "only the arrival is an add");
        Assert.AreSame(before[0], tree.Roots[0], "the nodes that were there are the nodes still there");
        Assert.AreSame(before[1], tree.Roots[1]);
    }

    /// <summary>Spawns a grandchild through the child scope a row stands for, as the child's own agent would.</summary>
    private static string SpawnGrandchild(SubAgentFixture fx, string parentId, string task)
    {
        var id = fx.DescendantSubAgents(parentId).TrySpawn(new SubAgentRequest { Task = task }, out var refusal);

        if (id is null)
            throw new InvalidOperationException($"The nested spawn was refused: {refusal}");
        return id;
    }
}
