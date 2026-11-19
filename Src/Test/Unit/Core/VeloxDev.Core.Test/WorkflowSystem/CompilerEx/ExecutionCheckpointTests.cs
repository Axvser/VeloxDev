using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// Checkpoint and resume: a run writes its place after each node succeeds, and a later run picks it up — the nodes
/// the checkpoint records as done are not driven again, and what they produced is still there for the nodes that
/// come after them.
/// </summary>
[TestClass]
public class ExecutionCheckpointTests
{
    private static async Task<(RuntimeContext Session, InMemoryCheckpointStore Store)> RunAsync(
        ProbeNode start, ExecutionCheckpoint? resumeFrom = null, CancellationToken ct = default)
    {
        var store = new InMemoryCheckpointStore();
        var context = new RuntimeContext { CheckpointStore = store };
        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(start), context, ct, resumeFrom);
        return (context, store);
    }

    [TestMethod]
    public async Task ARunWithAStore_WritesTheRunsPlaceAfterEachNode()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        var c = new ProbeNode("c") { Handler = (_, _) => "C" };
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);

        var (session, store) = await RunAsync(a);

        var checkpoint = await store.LoadAsync(CancellationToken.None);
        Assert.IsNotNull(checkpoint, "a configured store must end up holding the run's place");
        Assert.HasCount(3, checkpoint.Shape, "the shape names every node the graph drives");
        Assert.HasCount(3, checkpoint.Outputs, "and every one of them produced something");
        Assert.AreEqual(1, checkpoint.Attempt);
        Assert.AreEqual("C", checkpoint.Data, "the payload is the last node's output");
        Assert.AreEqual("Completed", session.Status);
    }

    /// <summary>
    /// `p → {a, b} → join`, stopped while the group was half done: `a` stops the run as it finishes. The resume
    /// must drive the branch that never ran and the join behind it, leave `p` and `a` alone, and still have `a`'s
    /// output in the group the join reads — that last part is what proves the outputs were carried over rather
    /// than merely skipped.
    /// </summary>
    [TestMethod]
    public async Task Resuming_SkipsWhatTheCheckpointRecords_AndKeepsWhatThoseNodesProduced()
    {
        using var cts = new CancellationTokenSource();
        var p = new ProbeNode("p") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a") { Handler = (_, _) => { cts.Cancel(); return "A"; } };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        object? seen = null;
        var join = new ProbeNode("join") { Handler = (ctx, _) => { seen = ctx.Data; return "J"; } };
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);

        var (stopped, store) = await RunAsync(p, ct: cts.Token);
        Assert.AreEqual(RunOutcome.Cancelled, stopped.Outcome);
        Assert.HasCount(1, p.Calls);
        Assert.HasCount(1, a.Calls);
        Assert.IsEmpty(b.Calls, "the second branch never started");

        var checkpoint = await store.LoadAsync(CancellationToken.None);
        Assert.IsNotNull(checkpoint);

        var resumed = new RuntimeContext { CheckpointStore = store };
        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(p), resumed, CancellationToken.None, checkpoint);

        Assert.AreEqual("Completed", resumed.Status);
        Assert.HasCount(1, p.Calls, "a node the checkpoint records as done must not be driven again");
        Assert.HasCount(1, a.Calls, "its effect is already there — running it twice would do it twice");
        Assert.HasCount(1, b.Calls, "the branch that never ran is the one the resume drives");
        Assert.HasCount(1, join.Calls);
        var group = Assert.IsInstanceOfType<IGroupData>(seen, "the join receives the group of upstream outputs");
        Assert.AreEqual(2, group.Count, "both upstreams are in it — one restored, one just produced");
        Assert.IsTrue(group.TryGetValue(a, out var fromA) && Equals(fromA, "A"),
            "the restored output is what the join reads for the node that was skipped");
        Assert.IsTrue(group.TryGetValue(b, out var fromB) && Equals(fromB, "B"));
    }

    /// <summary>
    /// A checkpoint belongs to one graph. Resuming onto another is refused before the session is touched, because
    /// the alternative — driving this graph's nodes with that graph's outputs — is silent nonsense.
    /// </summary>
    [TestMethod]
    public async Task ACheckpointTakenOverAnotherGraph_IsRefused_AndTheSessionIsLeftAlone()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(a, b);
        var (_, store) = await RunAsync(a);
        var checkpoint = await store.LoadAsync(CancellationToken.None);

        var longer = new ProbeNode("c") { Handler = (_, _) => "C" };
        ProbeGraph.Wire(b, longer);
        var session = new RuntimeContext();
        var drivenSoFar = a.Calls.Count;   // 那张图跑过一次，节点是同一批对象

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), session, CancellationToken.None, checkpoint));

        Assert.AreEqual("Idle", session.Status, "a refused resume must not leave the session looking like it ran");
        Assert.IsFalse(session.IsRunning);
        Assert.HasCount(drivenSoFar, a.Calls, "and nothing may be driven");
    }

    /// <summary>
    /// A group payload is keyed by node reference, which cannot be written down — and would drag the whole tree
    /// along if it were. The snapshot files it by node key instead.
    /// </summary>
    [TestMethod]
    public async Task AGroupPayload_IsStoredByNodeKey_NotByNodeReference()
    {
        var p = new ProbeNode("p") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        var join = new ProbeNode("join") { Handler = (ctx, _) => ctx.Data };   // 把收到的汇合载荷原样返回
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);

        var (_, store) = await RunAsync(p);

        var checkpoint = await store.LoadAsync(CancellationToken.None);
        Assert.IsNotNull(checkpoint);
        var payload = Assert.IsInstanceOfType<Dictionary<string, object?>>(checkpoint.Data,
            "the group is filed as a plain dictionary keyed by node key");
        Assert.HasCount(2, payload, "both upstreams are in it");
        Assert.IsTrue(payload.Values.Any(v => Equals(v, "A")) && payload.Values.Any(v => Equals(v, "B")),
            $"and their outputs came along; got: {string.Join(", ", payload.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [TestMethod]
    public async Task AStoreThatThrows_DoesNotStopTheRun()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(a, b);
        var context = new RuntimeContext { CheckpointStore = new ThrowingStore() };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual("Completed", context.Status, "a place you cannot write down is not a reason to stop working");
        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[Checkpoint]", StringComparison.Ordinal)),
            $"the store's own failure has to be visible somewhere; got: {string.Join(" | ", context.Logs)}");
    }

    private sealed class ThrowingStore : IExecutionCheckpointStore
    {
        public Task SaveAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken)
            => throw new InvalidOperationException("the disk is full");

        public Task<ExecutionCheckpoint?> LoadAsync(CancellationToken cancellationToken)
            => Task.FromResult<ExecutionCheckpoint?>(null);
    }
}
