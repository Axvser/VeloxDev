using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

// Reverse (sink-driven) compilation — reached from CompileAsync(role: CompileRole.Terminal): compile the minimal
// producer subgraph needed to compute a target node's output — no controller / start node required.
//
// Semantics: the compiled artifact is the forward decomposition of the target's ANCESTOR CONE (all nodes that can
// reach the target through valid output edges), executed from the cone's own entry frontier (nodes with no in-cone
// input). Routers on the way KEEP their real branch semantics: only the branch that leads into the cone is
// compiled, so a router that actually decides on a sibling branch at runtime simply does not reach the target —
// the flow ends there, exactly like forward semantics. The target is never fabricated; reachability is reported
// by the caller (see RuntimeContext.Target / TargetReached).

public sealed partial class CompilerViewModel
{
    private sealed class ConeInfo(
        ISet<IWorkflowNodeViewModel> nodes,
        IReadOnlyDictionary<IWorkflowNodeViewModel, IReadOnlyList<IWorkflowNodeViewModel>> inConePredecessors)
    {
        public ISet<IWorkflowNodeViewModel> Nodes { get; } = nodes;
        public IReadOnlyDictionary<IWorkflowNodeViewModel, IReadOnlyList<IWorkflowNodeViewModel>> InConePredecessors { get; } = inConePredecessors;
    }

    /// <summary>
    /// Reverse BFS from the target over slot.Sources edges, keeping only edges whose owner's AccessAsync gate
    /// accepts them (same "unconnected when rejected" rule the forward compiler applies). Returns the ancestor cone
    /// plus, per node, its in-cone predecessors (distinct source nodes).
    /// </summary>
    private async Task<ConeInfo> BuildAncestorConeAsync(IWorkflowNodeViewModel target, CancellationToken ct)
    {
        var comparer = WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance;
        var cone = new HashSet<IWorkflowNodeViewModel>(comparer);
        var predecessors = new Dictionary<IWorkflowNodeViewModel, IReadOnlyList<IWorkflowNodeViewModel>>(comparer);
        var predSets = new Dictionary<IWorkflowNodeViewModel, List<IWorkflowNodeViewModel>>(comparer);
        var queue = new Queue<IWorkflowNodeViewModel>();
        cone.Add(target);
        queue.Enqueue(target);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var node = queue.Dequeue();
            foreach (var slot in node.Slots.Where(s => s is not null))
            {
                foreach (var source in slot!.Sources ?? [])
                {
                    ct.ThrowIfCancellationRequested();
                    if (source.Parent is not IWorkflowNodeViewModel sender) continue;

                    var helper = sender.GetHelper();
                    if (helper is null) continue;   // mirrors the forward walk: no helper ⇒ treated as unconnected
                    var accessCtx = new CompileContext { Sender = source, Receiver = slot };
                    if (!await helper.AccessAsync(accessCtx, ct).ConfigureAwait(false))
                        continue;                   // rejected edge ⇒ not an ancestor dependency

                    if (!predSets.TryGetValue(node, out var list))
                        predSets[node] = list = [];
                    if (!list.Any(p => ReferenceEquals(p, sender)))
                        list.Add(sender);

                    if (cone.Add(sender))
                        queue.Enqueue(sender);
                }
            }
        }

        foreach (var entry in predSets)
            predecessors[entry.Key] = entry.Value;
        return new ConeInfo(cone, predecessors);
    }

    /// <summary>
    /// Compiles the restricted cone into a single CompiledGraph. One entry → plain forward compile from it
    /// (routers keep real BranchSegment semantics, but only branches that lead into the cone are compiled).
    /// Several independent entries → each compiles into a fan-out branch, and after all of them the common join
    /// (the cone's funnel point, usually the target) continues as a normal chain.
    /// </summary>
    private async Task<CompiledGraph> CompileConeAsync(
        IWorkflowNodeViewModel target, ConeInfo info, CancellationToken ct)
    {
        var state = new CompileState { Cone = info.Nodes };

        // Entry frontier: cone nodes with no in-cone predecessors (controllers / data sources / no-input nodes).
        var entries = info.Nodes
            .Where(n => !info.InConePredecessors.TryGetValue(n, out var preds) || preds.Count == 0)
            .ToList();
        if (entries.Count == 0)
            entries.Add(target);   // defensive: the target alone is always its own frontier

        if (entries.Count == 1)
            return await CompileGraphAsync(entries[0], state, ct);

        // Multiple independent producers funnel into the target. Each entry compiles up to the shared funnel join;
        // after all of them the common join (its inputs span several entries) is driven as one continuation.
        var branches = new List<CompiledGraph>();
        var exits = new List<IWorkflowNodeViewModel?>();
        foreach (var entry in entries)
        {
            var sub = await CompileGraphAsync(entry, state, ct);
            branches.Add(sub);
            exits.Add(LastNode(sub));
        }

        var common = CommonNext(exits, info.Nodes);
        if (common is null)
        {
            throw new InvalidOperationException(
                "CompileAsync(CompileRole.Terminal) cannot express this ancestor cone: its independent producers " +
                "do not funnel into a single common join before the target '" + target.GetType().Name + "'. " +
                "Refactor so the target's producers converge at one join (series-parallel cone).");
        }

        var distinctExits = exits.Where(e => e is not null)
            .Cast<IWorkflowNodeViewModel>()
            .Distinct(WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance)
            .ToList();
        if (distinctExits.Count > 1)
            state.JoinInputs[common] = distinctExits;

        // The join node (target or an ancestor of it) is the new single root; it was not visited by any branch walk.
        var tail = await CompileGraphAsync(common, state, ct);

        var entriesOut = new ObservableCollection<CompileSegment>
        {
            new ParallelSegment { Branches = new ObservableCollection<CompiledGraph>(branches) },
        };
        foreach (var e in tail.Entries)
            entriesOut.Add(e);
        return new CompiledGraph { Entries = entriesOut };
    }
}
