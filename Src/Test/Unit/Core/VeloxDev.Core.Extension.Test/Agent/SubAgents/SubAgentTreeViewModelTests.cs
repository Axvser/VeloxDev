using System;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI.SubAgents;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The panel: a flat roster, several scopes deep, projected onto one tree.
/// <para>
/// What is tested here is the projection and its upkeep rather than any rendering. The roster a scope keeps
/// is deliberately flat and holds only that scope's own children — that is what keeps one branch from seeing
/// another — so the tree is the one place the whole shape exists at all. The claims that matter are that it
/// is built from those flat pieces, that a rebuild does not throw away what the user was looking at, and
/// that closing the panel is not a decision about the work it was showing.
/// </para>
/// </summary>
[TestClass]
public class SubAgentTreeViewModelTests
{
    [TestMethod]
    public async Task AnEmptyRoster_IsAnEmptyTree()
    {
        await using var fx = new SubAgentFixture();
        using var tree = new SubAgentTreeViewModel(fx.SubAgents);

        Assert.IsEmpty(tree.Roots);
        Assert.IsTrue(tree.IsEmpty);
        Assert.IsTrue(tree.IsIdle, "nothing running is idle, and that is not the same as having nothing");
        Assert.IsFalse(tree.HasFailed);
        Assert.AreEqual(0, tree.TotalCount);
    }

    [TestMethod]
    public async Task AFlatRoster_BecomesATree()
    {
        // The one claim that needs more than one scope: siblings sit at the top, and a grandchild hangs off
        // the row that dispatched it rather than beside it.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        var first = fx.Spawn("the first job", ("name", "alpha"));
        var grand = SpawnGrandchild(fx, first, "the nested job");
        fx.Spawn("the second job", ("name", "beta"));

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);

        Assert.AreEqual(2, tree.Roots.Count, "the grandchild is not a root");
        Assert.AreEqual(3, tree.TotalCount, "but it is counted");
        Assert.IsTrue(tree.Roots.All(n => n.Row is not null),
            "every root here is a real spawn; only the synthetic scope root carries no row");
        CollectionAssert.AreEqual(
            new[] { "alpha", "beta" }, tree.Roots.Select(n => n.Row!.Name).ToArray(),
            "roots keep spawn order, so the tree does not reshuffle under the user");

        var parent = tree.Roots[0];
        Assert.IsTrue(parent.HasChildren);
        Assert.IsFalse(tree.Roots[1].HasChildren);

        var child = parent.Children.Single();
        Assert.AreEqual(grand, child.Id);
        Assert.AreEqual(2, child.Depth);
        Assert.AreSame(parent, child.Parent, "the back-reference is what a template binds a level up with");
        Assert.AreSame(tree.ScopeRoot, parent.Parent, "a top-level row hangs off the scope's own node");
        Assert.IsNull(tree.ScopeRoot.Parent, "and the scope is the top of the tree, so it has none");
        Assert.IsTrue(tree.ScopeRoot.IsScopeRoot);
        Assert.AreSame(tree.ScopeRoot.Children, tree.Roots, "Roots is that node's children, not a copy");
        Assert.HasCount(1, tree.Tree, "the tree is rendered from one node, the scope");
        Assert.AreSame(tree.ScopeRoot, tree.Tree[0]);
    }

    [TestMethod]
    public async Task AGrandchildAppearing_HangsOffItsParent()
    {
        // The projection is rebuilt from several scopes, so a level that did not exist when the panel was
        // opened has to be discovered on a later pass rather than only at construction.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());
        var child = fx.Spawn("the outer job");

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);
        Assert.IsFalse(tree.Roots.Single().HasChildren);

        var grand = SpawnGrandchild(fx, child, "the nested job");

        Assert.AreEqual(2, tree.TotalCount);
        var node = tree.Roots.Single();
        Assert.AreEqual(grand, node.Children.Single().Id);
        Assert.IsTrue(node.HasChildren, "and the flag a template hides the expander with follows");
    }

    [TestMethod]
    public async Task AChildFinishing_RebuildsTheCounts()
    {
        var gate = new GateChatClient("the answer");
        await using var fx = new SubAgentFixture(client: gate);

        var id = fx.Spawn("the job");
        gate.WaitForCalls(1);
        using var tree = new SubAgentTreeViewModel(fx.SubAgents);

        Assert.AreEqual(1, tree.RunningCount);
        Assert.IsFalse(tree.IsIdle);

        gate.ReleaseAll();
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        Assert.AreEqual(1, tree.CompletedCount);
        Assert.AreEqual(0, tree.RunningCount);
        Assert.IsTrue(tree.IsIdle);
        Assert.IsFalse(tree.HasFailed);
        var single = tree.Roots.Single().Row;
        Assert.IsNotNull(single, "the only root is a real spawn, so it carries a row");
        Assert.AreEqual("the answer", single.Result);
    }

    [TestMethod]
    public async Task AStoppedChild_IsNotCountedAsAFailedOne()
    {
        // Two children, two finishes, in one tree — so the counters are shown to be per-outcome rather than
        // one "not running" tally. A panel that folded a stop into a failure would teach the user to
        // distrust the control that stops.
        var gate = new GateChatClient("the answer");
        await using var fx = new SubAgentFixture(client: gate);

        var stopped = fx.Spawn("no longer worth it");
        var finished = fx.Spawn("the one that counts");
        gate.WaitForCalls(2);

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);
        fx.Invoke("CancelSubAgent", ("id", stopped));
        gate.ReleaseAll();
        SubAgentFixture.WaitFor(() => tree.IsIdle);

        Assert.AreEqual(2, tree.TotalCount);
        Assert.AreEqual(1, tree.CancelledCount);
        Assert.AreEqual(1, tree.CompletedCount);
        Assert.AreEqual(0, tree.FailedCount);
        Assert.IsFalse(tree.HasFailed, "a cancellation is not something to be alarmed about");
        Assert.IsFalse(fx.RowOf(stopped).HasError);
        Assert.AreEqual("the answer", fx.RowOf(finished).Result);
    }

    [TestMethod]
    public async Task AFailedChild_IsCountedAndCarriesItsReason()
    {
        await using var fx = new SubAgentFixture(client: new FaultingChatClient());
        var id = fx.Spawn("doomed");

        using var tree = new SubAgentTreeViewModel(fx.SubAgents);
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Failed);

        Assert.AreEqual(1, tree.FailedCount);
        Assert.IsTrue(tree.HasFailed, "which is what a panel raises an alarm on");
        Assert.AreEqual(0, tree.CancelledCount, "a failure is not a stop either way round");
        Assert.IsTrue(tree.IsIdle, "it is finished, however badly");
        Assert.AreEqual(0, tree.CancelledCount, "a failure is not a stop either way round");

        var row = tree.Roots.Single().Row;
        Assert.IsNotNull(row, "the only root is a real spawn, so it carries a row");
        Assert.IsTrue(row.HasError, "the row says something went wrong");
        StringAssert.Contains(row.Error, "the model is unreachable",
            "and carries the reason rather than only the fact, which is what a panel shows");
    }

    [TestMethod]
    public async Task ARebuild_KeepsTheNodeItAlreadyHad()
    {
        // Node identity has to survive a rebuild or the panel is unusable: every child changing state would
        // collapse the tree and drop whatever the user had selected, in a graph that is nothing but changes.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(client: new InstantChatClient(), ui: ui);

        var first = fx.Spawn("the first job");
        SubAgentFixture.WaitFor(() => fx.RowOf(first).IsFinished);

        SubAgentTreeViewModel? tree = null;
        SubAgentTreeNodeViewModel? node = null;
        ui.Send(_ =>
        {
            tree = new SubAgentTreeViewModel(fx.SubAgents);
            node = tree.Roots.Single();
            node.ToggleExpand();
        }, null);

        fx.Spawn("the second job");
        SubAgentFixture.WaitFor(() => fx.SubAgents.Snapshot.Count == 2);

        ui.Send(_ =>
        {
            Assert.AreEqual(2, tree!.Roots.Count);
            Assert.AreSame(node, tree.Roots[0], "the node the panel already had is kept, not replaced");
            Assert.IsFalse(node!.IsExpanded, "which is what keeps a collapsed node collapsed");
            Assert.AreEqual("▸", node.ExpandGlyph);
        }, null);
    }

    [TestMethod]
    public async Task AChangeFromAnotherThread_IsMarshalledOntoThePanels()
    {
        // The panel renders on the thread it was built on, and a scope can be changed from anywhere — a host
        // reconfiguring one, or a child finishing before the roster is bound. The rebuild is queued rather
        // than run, so the collection it mutates is only ever touched where the panel lives.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(client: new InstantChatClient(), ui: ui);

        SubAgentTreeViewModel? tree = null;
        ui.Send(_ => tree = new SubAgentTreeViewModel(fx.SubAgents), null);

        var before = ui.RanOnThreads.Count;
        fx.SubAgents.WithSpawnBudget(70);          // announced here, on the test's thread
        SubAgentFixture.WaitFor(() => ui.RanOnThreads.Count > before);

        Assert.IsTrue(ui.PostCount > 0, "it was posted rather than run where it was raised");
        foreach (var thread in ui.RanOnThreads)
            Assert.AreEqual(ui.PumpThreadId, thread, "and nothing drained on a thread other than the pump");

        ui.Send(_ => tree!.Dispose(), null);
    }

    [TestMethod]
    public async Task Dispose_LeavesTheChildrenRunning()
    {
        // Closing a panel is not a decision about the work the panel was showing. The scope still holds the
        // child, it is still running, and it will still finish.
        var gate = new GateChatClient("the answer");
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(client: gate, ui: ui);

        var id = fx.Spawn("still going");
        gate.WaitForCalls(1);

        SubAgentTreeViewModel? tree = null;
        ui.Send(_ => tree = new SubAgentTreeViewModel(fx.SubAgents), null);
        Assert.AreEqual(1, tree!.TotalCount);

        ui.Send(_ => tree!.Dispose(), null);
        Assert.IsEmpty(tree!.Roots, "the panel lets go of what it was showing");

        Assert.IsTrue(fx.RowOf(id).IsRunning, "but the child it was showing is untouched");
        gate.ReleaseAll();
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);
        Assert.AreEqual("the answer", fx.RowOf(id).Result);
    }

    [TestMethod]
    public async Task AfterDispose_NothingRebuilds()
    {
        var gate = new GateChatClient();
        await using var fx = new SubAgentFixture(client: gate);

        var id = fx.Spawn("the job");
        gate.WaitForCalls(1);
        using var tree = new SubAgentTreeViewModel(fx.SubAgents);
        tree.Dispose();

        gate.ReleaseAll();
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        Assert.AreEqual(0, tree.TotalCount, "a detached panel does not keep up with a scope it let go of");
        Assert.IsEmpty(tree.Roots);
        Assert.IsTrue(tree.IsEmpty, "and the counts agree with the nodes — a total left standing would "
            + "describe children this panel no longer holds, and it could never be refreshed into agreement");
    }

    [TestMethod]
    public async Task Dispose_DetachesFromEveryScopeItVisited()
    {
        // A tree watches one scope per level, and a level is only known once a rebuild has walked into it —
        // so unsubscribing from only the scope it was built over would leak a handler per grandchild.
        // Asserted on what was posted rather than on what was drained: a post happens synchronously on the
        // thread that raises the change, so "nothing was posted" needs no settling and cannot be raced by a
        // callback still on its way through the pump.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(client: new InstantChatClient(), ui: ui);

        var child = fx.Spawn("the outer job");
        SubAgentFixture.WaitFor(() => fx.RowOf(child).IsFinished);

        var grand = SpawnGrandchild(fx, child, "the nested job");

        // Two different things, both needed: the grandchild's row sits in the child's roster, while the
        // scope the tree subscribed to a level lower is the grandchild's own subsystem. Both runs have to be
        // over before the count is taken, or one of their own posts lands after it.
        var childRoster = fx.DescendantSubAgents(child);
        var grandScope = fx.DescendantSubAgents(child, grand);
        SubAgentFixture.WaitFor(() => childRoster.Snapshot.Count == 1 && childRoster.Snapshot[0].IsFinished);

        SubAgentTreeViewModel? tree = null;
        ui.Send(_ => tree = new SubAgentTreeViewModel(fx.SubAgents), null);
        ui.Send(_ => Assert.AreEqual(2, tree!.TotalCount, "both levels were walked into"), null);
        ui.Send(_ => tree!.Dispose(), null);

        var posted = ui.PostCount;
        fx.SubAgents.WithSpawnBudget(70);
        grandScope.WithSpawnBudget(71);

        Assert.AreEqual(posted, ui.PostCount,
            "neither change queued a rebuild — the handler on each of the two scopes was detached");
    }

    [TestMethod]
    public async Task DisposingTwice_IsHarmless()
    {
        await using var fx = new SubAgentFixture();
        var tree = new SubAgentTreeViewModel(fx.SubAgents);

        tree.Dispose();
        tree.Dispose();
    }

    [TestMethod]
    public async Task AToggle_FlipsTheGlyph()
    {
        await using var fx = new SubAgentFixture(client: new InstantChatClient());
        fx.Spawn("the job");
        using var tree = new SubAgentTreeViewModel(fx.SubAgents);

        var node = tree.Roots.Single();
        Assert.IsTrue(node.IsExpanded, "a node starts open: a tree nobody has touched should show its shape");
        Assert.AreEqual("▾", node.ExpandGlyph);

        node.ToggleExpand();

        Assert.IsFalse(node.IsExpanded);
        Assert.AreEqual("▸", node.ExpandGlyph, "the glyph follows the state rather than being set beside it");

        node.ToggleExpand();
        Assert.AreEqual("▾", node.ExpandGlyph);
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
