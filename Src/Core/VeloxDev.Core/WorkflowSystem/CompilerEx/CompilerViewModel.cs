using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The compile entry point: decomposes the sub-graph reachable from one start node (Controller) into several
/// compiled graphs (multi-graph semantics).
/// v1 decomposition algorithm (direct pruning, no cycles):
///  - linear segment (single input / single output) → ExecuteEntry;
///  - node implementing <see cref="ICompileTimeRouter"/> → BranchEntry (static branches are pruned by the current
///    key, dynamic branches are all kept);
///  - route key pointing to multiple downstream nodes → ParallelEntry (fan-out/join); no downstream → IsTerminal
///    terminal branch;
///  - the node all branch exits jointly point to → join point, used as the start of the next chain segment in the
///    parent graph (order carries an offset, not reset to zero);
///  - after compiling, every node implementing <see cref="ICompileTimeAware"/> is injected with a
///    <see cref="CompileContext"/>.
/// Runtime redirect is handled by nodes implementing <see cref="IRedirectable"/> (in-chain redirect); the compiled
/// graph itself is acyclic.
/// </summary>
public sealed partial class CompilerViewModel
{
    [VeloxProperty] private ObservableCollection<CompiledGraph> _graphs = [];

    public async Task<IReadOnlyList<CompiledGraph>> CompileAsync<T>(T component, CancellationToken ct = default)
        where T : IWorkflowViewModel
    {
        if (component is not IWorkflowNodeViewModel start)
            throw new ArgumentException(
                $"CompileAsync requires an IWorkflowNodeViewModel as the start node; received {component?.GetType().Name}.");

        var state = new CompileState();
        var graphs = new List<CompiledGraph> { await CompileGraphAsync(start, state, ct) };

        _graphs.Clear();
        foreach (var g in graphs) _graphs.Add(g);
        return graphs;
    }

    private async Task<CompiledGraph> CompileGraphAsync(
        IWorkflowNodeViewModel start, CompileState state, CancellationToken ct)
    {
        var entries = new List<ActionEntry>();
        var chain = new List<IWorkflowNodeViewModel>();
        var offset = state.Counter;
        var node = start;
        var resumedAfterBranch = false;   // Whether the current node was just resumed from a branch join point (should be treated as a new chain start)

        while (node != null)
        {
            // Join boundary: walking linearly into a multi-input node (not the graph start, not a branch-resume
            // point) → stop and hand back to the parent graph to continue from it. The boundary node is not marked
            // visited (it is not yet compiled; it belongs to the parent graph).
            if (!ReferenceEquals(node, start) && !resumedAfterBranch && HasMultipleInputs(node))
                break;

            // Nodes already compiled are not processed again (acyclic graph, avoids duplicate compilation).
            if (!state.Visited.Add(node))
                break;
            resumedAfterBranch = false;

            if (node is ICompileTimeRouter router)
            {
                FlushChain(entries, chain, state, offset);
                AttachCompileContext(node, state.Counter, 0, offset, state);
                state.Counter++;

                var routeTable = await router.GetRouteTable();
                var currentKey = await router.ResolveRouteKey(null);   // compile-time payload = null
                var isDynamic = currentKey is null;
                var options = new ObservableCollection<BranchOption>();
                var exits = new List<IWorkflowNodeViewModel?>();

                // Generic route compilation: each key's sub-graph may be a single path or a fan-out (ParallelEntry).
                // No downstream → terminal branch; single target → normal sub-graph; multiple targets → fan-out
                // (parallel group, join point = CommonNext).
                foreach (var kv in routeTable)
                {
                    var label = kv.Key?.ToString() ?? "?";
                    if (kv.Value is null || kv.Value.Count == 0)
                    {
                        options.Add(new BranchOption { Key = kv.Key, Label = label, Graph = null, IsTerminal = true });
                        continue;
                    }
                    if (kv.Value.Count == 1)
                    {
                        var target = kv.Value[0];
                        if (target is null) continue;
                        var sub = await CompileGraphAsync(target, state, ct);
                        options.Add(new BranchOption { Key = kv.Key, Label = label, Graph = sub });
                        exits.Add(LastNode(sub));
                        continue;
                    }
                    // Multiple targets → fan-out: compile each sub-graph into a ParallelEntry.
                    var branches = new List<CompiledGraph>();
                    var subExits = new List<IWorkflowNodeViewModel?>();
                    foreach (var t in kv.Value)
                    {
                        if (t is null) continue;
                        var sub = await CompileGraphAsync(t, state, ct);
                        branches.Add(sub);
                        subExits.Add(LastNode(sub));
                    }
                    options.Add(new BranchOption
                    {
                        Key = kv.Key,
                        Label = label,
                        Graph = new CompiledGraph
                        {
                            Entries = new ObservableCollection<ActionEntry>
                            {
                                new ParallelEntry { Branches = new ObservableCollection<CompiledGraph>(branches) },
                            }
                        },
                    });
                    // Add the last node of each fan-out branch to the exits; CommonNext will compute their common downstream (join point).
                    exits.AddRange(subExits);
                }

                // Static mode (key known at compile-time): downstream nodes in the full topology not on an active
                // branch get a "reset signal" (CompileContext.Order = -1, absolute stop) — every node is reached
                // by the compiler in both modes.
                if (!isDynamic)
                {
                    var liveTargets = new HashSet<IWorkflowNodeViewModel>(
                        routeTable.Values.Where(v => v is not null).SelectMany(v => v!)
                            .Where(t => t is not null));
                    foreach (var target in AllTargets(node))
                    {
                        if (target is null || liveTargets.Contains(target)) continue;
                        MarkStoppedBranch(target, state);
                    }
                }

                entries.Add(new BranchEntry
                {
                    Router = node,
                    Options = options,
                    IsDynamic = isDynamic,
                    // Route key locked at compile-time: in Static mode runtime relies on it (the value selected at
                    // compile time); in Dynamic mode it is null and re-resolved at runtime.
                    CompileKey = currentKey,
                });

                // The join point after a branch: the next node all active branch exits jointly point to.
                node = CommonNext(exits);
                if (node is not null)
                {
                    // Join registration: write each branch exit (input source) into JoinInputs so the node's
                    // compile identity can backfill InputNodes, and the runtime aggregates them into a GroupData
                    // injected as Data. Single-input joins keep bare Data chaining semantics.
                    var distinctExits = exits.Where(e => e is not null)
                        .Cast<IWorkflowNodeViewModel>()
                        .Distinct(WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance)
                        .ToList();
                    if (distinctExits.Count > 1)
                        state.JoinInputs[node] = distinctExits;
                }
                resumedAfterBranch = node is not null;
                continue;
            }

            // Linear node
            chain.Add(node);
            var validTargets = await GetValidTargetsAsync(node, state, ct);
            var next = validTargets.Count == 1 ? validTargets[0] : null;
            if (next is null || ReferenceEquals(next, node))
            {
                FlushChain(entries, chain, state, offset);

                // Plain-node fan-out: a non-router node with several valid downstream targets broadcasts its
                // result to all of them as a ParallelEntry, then continues from their common join point
                // (same semantics as a router's single-key fan-out).
                if (validTargets.Count > 1)
                {
                    var branches = new List<CompiledGraph>();
                    var exits = new List<IWorkflowNodeViewModel?>();
                    foreach (var t in validTargets)
                    {
                        var sub = await CompileGraphAsync(t, state, ct);
                        branches.Add(sub);
                        exits.Add(LastNode(sub));
                    }
                    entries.Add(new ParallelEntry { Branches = new ObservableCollection<CompiledGraph>(branches) });

                    node = CommonNext(exits);
                    if (node is not null)
                    {
                        var distinctExits = exits.Where(e => e is not null)
                            .Cast<IWorkflowNodeViewModel>()
                            .Distinct(WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance)
                            .ToList();
                        if (distinctExits.Count > 1)
                            state.JoinInputs[node] = distinctExits;
                    }
                    resumedAfterBranch = node is not null;
                    continue;
                }
                break;
            }
            node = next;
        }

        FlushChain(entries, chain, state, offset);
        return new CompiledGraph { Entries = new ObservableCollection<ActionEntry>(entries) };
    }

    /// <summary>Flushes the current linear chain into an ExecuteEntry and assigns each node in the chain a compile identity.</summary>
    private static void FlushChain(List<ActionEntry> entries, List<IWorkflowNodeViewModel> chain,
        CompileState state, int offset)
    {
        if (chain.Count == 0) return;
        for (int i = 0; i < chain.Count; i++)
            AttachCompileContext(chain[i], state.Counter + i, i, offset, state);
        state.Counter += chain.Count;
        entries.Add(new ExecuteEntry { Nodes = new ObservableCollection<IWorkflowNodeViewModel>(chain) });
        chain.Clear();
    }

    private static void AttachCompileContext(IWorkflowNodeViewModel node, int order, int chainIndex, int offset,
        CompileState state)
    {
        if (node is ICompileTimeAware aware)
            aware.AttachCompileTimeContext(new CompileContext
            {
                Order = order,
                ChainIndex = chainIndex,
                Offset = offset,
                InputNodes = state.JoinInputs.TryGetValue(node, out var inputs) ? inputs : null,
            });
    }

    /// <summary>
    /// In static mode, starting from a skipped target, walks the full topology and sends each node a "reset signal"
    /// (Order = -1). Stops at a join point (multi-input, part of the main line) or an already active node; nodes are
    /// added to visited so the main line does not process them again.
    /// </summary>
    private static void MarkStoppedBranch(IWorkflowNodeViewModel start, CompileState state)
    {
        var queue = new Queue<IWorkflowNodeViewModel>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (state.Visited.Contains(n)) continue;
            if (!ReferenceEquals(n, start) && HasMultipleInputs(n)) continue;
            state.Visited.Add(n);
            AttachCompileContext(n, -1, -1, 0, state);
            foreach (var t in AllTargets(n))
                if (t is not null) queue.Enqueue(t);
        }
    }

    /// <summary>All of the node's downstream targets (the Targets of its output slots, deduplicated).</summary>
    private static IEnumerable<IWorkflowNodeViewModel> AllTargets(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => s is not null)
            .SelectMany(s => s!.Targets ?? [])
            .Select(t => t.Parent)
            .OfType<IWorkflowNodeViewModel>()
            .Distinct();

    private static IWorkflowNodeViewModel? LastNode(CompiledGraph? graph)
    {
        if (graph is null) return null;
        for (int i = graph.Entries.Count - 1; i >= 0; i--)
        {
            var e = LastNode(graph.Entries[i]);
            if (e is not null) return e;
        }
        return null;
    }

    private static IWorkflowNodeViewModel? LastNode(ActionEntry entry) => entry switch
    {
        ExecuteEntry exec when exec.Nodes.Count > 0 => exec.Nodes[exec.Nodes.Count - 1],
        BranchEntry branch when branch.Options.Count > 0 => LastNode(branch.Options[branch.Options.Count - 1].Graph),
        ParallelEntry par when par.Branches.Count > 0 => LastNode(par.Branches[par.Branches.Count - 1]),
        _ => null,
    };

    /// <summary>The common downstream (join point) of all branch exits; returns null when there is no common downstream (each branch ends on its own).</summary>
    private static IWorkflowNodeViewModel? CommonNext(List<IWorkflowNodeViewModel?> exits)
    {
        IWorkflowNodeViewModel? common = null;
        var first = true;
        foreach (var exit in exits)
        {
            if (exit is null) continue;
            var next = SingleTarget(exit);
            if (first) { common = next; first = false; }
            else if (!ReferenceEquals(next, common)) { common = null; break; }
        }
        return common;
    }

    private static bool HasMultipleInputs(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => s is not null)
            .SelectMany(s => s!.Sources ?? [])
            .Select(s => s.Parent)
            .OfType<IWorkflowNodeViewModel>()
            .Distinct()
            .Count() >= 2;

    private static IWorkflowNodeViewModel? SingleTarget(IWorkflowNodeViewModel node)
    {
        var targets = node.Slots.Where(s => s is not null)
            .SelectMany(s => s!.Targets ?? [])
            .Select(s => s.Parent)
            .OfType<IWorkflowNodeViewModel>()
            .Distinct()
            .ToList();
        return targets.Count == 1 ? targets[0] : null;
    }

    /// <summary>
    /// The "validity-aware" variant of <see cref="SingleTarget"/> for chain continuation: returns the downstream
    /// node uniquely reachable through valid output edges (exactly one after deduplication); otherwise null. Every
    /// output edge runs <see cref="IWorkflowNodeViewModelHelper.AccessAsync"/> static validation at compile-time
    /// with an <see cref="ICompileContext"/> — invalid edges are skipped per the runtime broadcast semantics
    /// (treated as unconnected) and do not enter the compiled graph.
    /// </summary>
    private async Task<IWorkflowNodeViewModel?> SingleTargetValidAsync(
        IWorkflowNodeViewModel node, CompileState state, CancellationToken ct)
    {
        var distinct = await GetValidTargetsAsync(node, state, ct);
        return distinct.Count == 1 ? distinct[0] : null;
    }

    /// <summary>
    /// All distinct downstream nodes reachable through valid output edges (each edge runs AccessAsync with an
    /// <see cref="ICompileContext"/>; invalid edges are skipped per the runtime broadcast semantics).
    /// Returns 0 for a leaf node, 1 for a linear chain, &gt;1 for a plain-node fan-out.
    /// </summary>
    private async Task<List<IWorkflowNodeViewModel>> GetValidTargetsAsync(
        IWorkflowNodeViewModel node, CompileState state, CancellationToken ct)
    {
        var validTargets = new List<IWorkflowNodeViewModel>();
        foreach (var sender in node.Slots.Where(s => s is not null))
        {
            foreach (var receiver in sender!.Targets ?? [])
            {
                ct.ThrowIfCancellationRequested();
                var target = receiver.Parent as IWorkflowNodeViewModel;
                if (target is null) continue;

                var helper = node.GetHelper();
                if (helper is null) continue;

                // Compile-time placeholder identity: Order uses the current cursor (the sender node is not yet numbered); Sender/Receiver hold the edge to validate.
                var compileCtx = new CompileContext
                {
                    Order = state.Counter,
                    ChainIndex = -1,
                    Offset = 0,
                    Sender = sender,
                    Receiver = receiver,
                };
                if (!await helper.AccessAsync(compileCtx, ct).ConfigureAwait(false))
                    continue;
                validTargets.Add(target);
            }
        }
        return validTargets.Distinct().ToList();
    }

    /// <summary>Compile cursor: a global order counter plus a visited set (avoids ref parameters so it can be shared across async recursion).</summary>
    private sealed class CompileState
    {
        public int Counter;
        public readonly HashSet<IWorkflowNodeViewModel> Visited = [];

        /// <summary>Join point → input source node list (registered from each branch exit at compile-time, so the join point's compile identity can backfill InputNodes).</summary>
        public readonly Dictionary<IWorkflowNodeViewModel, IReadOnlyList<IWorkflowNodeViewModel>> JoinInputs =
            new(WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);
    }
}
