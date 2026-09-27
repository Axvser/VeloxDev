using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Runtime engine: drives node execution along a compiled graph (<see cref="CompiledGraph"/>).
/// It does not rely on nodes broadcasting on their own — the engine "takes the data and drives the next node":
///  - ChainSegment: drives nodes one by one along a linear segment;
///  - BranchSegment: drives the router itself, then selects a branch via <see cref="ICompileTimeRouter.ResolveRouteKey"/> and drives the chosen subgraph;
///  - ParallelSegment: fan-out group, executes each branch in order (sequential order carries the "wait for all upstream" merge semantics).
/// When a node calls <see cref="IRuntimeContext.Error"/>/<see cref="IRuntimeContext.Warn"/> or throws inside ReceiveAsync,
/// it is treated as a redirect request: if the node implements <see cref="IRedirectable"/>, the engine re-runs the
/// whole graph with the returned compile state (CompileContext.Order), skipping nodes before the target (possibly cross-chain);
/// when the target is a Router it only re-routes without recomputing.
/// If the node does not implement IRedirectable, the whole flow ends with the standard -1 status.
/// Before each drive the engine injects <see cref="IRuntimeContext"/> into <see cref="IRuntimeAware"/> nodes.
/// </summary>
public sealed class RuntimeEngine
{
    public async Task RunAsync(CompiledGraph graph, IRuntimeContext context, CancellationToken ct)
    {
        if (graph is null || context is null) return;
        const int MaxRedirects = 50;
        context.IsRunning = true;
        context.Status = "Running";
        // Each RunAsync clears the output registry once; redirect re-runs do not clear it, stale
        // outputs are filtered by pass stamp (CollectGroupedInputs = this pass's outputs ∪ the
        // contract-preserved prefix before the redirect target).
        context.ResetOutputs();
        context.TargetReached = false;
        int? redirectTarget = null;
        var redirects = 0;
        try
        {
            // A redirect is uniformly implemented as "re-run the whole graph with the target Order":
            // nodes before the target are skipped (possibly cross-chain).
            while (true)
            {
                context.Attempt = redirects + 1;   // graph re-run count (increments per redirect)
                context.PendingRedirectTarget = null;
                context.ActiveRedirectTarget = redirectTarget;   // null on the first pass; output collection uses it to tell contract-preserved prefix from stale branches
                var terminated = await RunGraphAsync(graph, context, ct, redirectTarget);
                if (!terminated && context.PendingRedirectTarget is { } next)
                {
                    redirects++;
                    if (redirects > MaxRedirects)
                    {
                        context.Error($"Redirected more than {MaxRedirects} times. Aborting.");
                        throw new InvalidOperationException($"Redirected more than {MaxRedirects} times. Aborting.");
                    }
                    context.Log($"Redirecting to compile state #{next} (skipping prior nodes, re-executing).");
                    redirectTarget = next;
                    continue;
                }
                break;
            }
            context.Status = context.EndedWithError ? "Stopped" : "Completed";
        }
        catch (OperationCanceledException)
        {
            context.Status = "Stopped";
        }
        finally
        {
            context.IsRunning = false;
        }
    }

    /// <summary>Drives every entry of a graph. Returns true when the run ends here (terminal branch or error termination).</summary>
    private async Task<bool> RunGraphAsync(CompiledGraph? graph, IRuntimeContext? context, CancellationToken ct, int? redirectTarget)
    {
        if (graph is null || context is null) return false;
        foreach (var entry in graph.Entries)
        {
            ct.ThrowIfCancellationRequested();
            context.CurrentEntry = entry;
            bool terminated;
            switch (entry)
            {
                case ChainSegment exec:
                    terminated = await RunExecuteAsync(exec, context, ct, redirectTarget);
                    break;
                case BranchSegment branch:
                    terminated = await RunBranchAsync(branch, context, ct, redirectTarget);
                    break;
                case ParallelSegment parallel:
                    terminated = await RunParallelAsync(parallel, context, ct, redirectTarget);
                    break;
                default:
                    terminated = false;
                    break;
            }
            if (terminated) return true;
        }
        return false;
    }

    /// <summary>
    /// Drives a linear chain. On a cross-chain redirect, nodes with Order &lt; the target are skipped;
    /// a node error (Error/Warn/exception) is treated as a redirect request:
    /// with <see cref="IRedirectable"/> the node returns a redirect target (possibly cross-chain), which is set on
    /// <see cref="IRuntimeContext.PendingRedirectTarget"/> so RunAsync re-runs the whole graph with that target;
    /// without it the flow ends with status -1. Returns true when the flow ends early.
    /// </summary>
    private async Task<bool> RunExecuteAsync(ChainSegment exec, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
    {
        for (int i = 0; i < exec.Nodes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var node = exec.Nodes[i];
            if (node is null) continue;
            var order = NodeOrder(node);

            // Cross-chain redirect skip semantics: nodes before the target are not driven.
            if (redirectTarget is int t && order < t)
                continue;

            context.NodeIndex = i;
            context.RedirectRequested = false;
            try
            {
                await DriveAsync(node, context, ct);
            }
            catch (OperationCanceledException)
            {
                throw;   // cancellation is not a redirect
            }
            catch (Exception)
            {
                // A node exception in ReceiveAsync → DriveAsync already recorded context.Error and set
                // RedirectRequested; caught here to flow into the "redirect or end flow" logic below,
                // instead of aborting the whole graph.
            }

            if (!context.RedirectRequested) continue;

            // Node errored but does not implement IRedirectable → the whole flow ends with status -1.
            if (node is not IRedirectable redirectable)
            {
                context.CurrentOrder = -1;
                context.EndedWithError = true;
                context.Error("Node reported an error but does not implement IRedirectable; the flow ends (status -1).");
                return true;
            }

            // With IRedirectable → its interface decides the redirect target (possibly cross-chain).
            // Only a predecessor state (Order < current) is accepted.
            var target = await redirectable.ResolveRedirectAsync(context, ct);
            if (target is { } targetOrder && targetOrder < order)
            {
                context.PendingRedirectTarget = targetOrder;
            }
            else
            {
                context.Log($"Redirect target #{target} is not a predecessor or is invalid; ignored.");
            }
        }
        return false;
    }

    /// <summary>
    /// Drives a branch. On a cross-chain redirect: when the target is before the branch → the whole
    /// branch is skipped; when the target is the router itself → **re-route only**, without recomputing
    /// (the router's ReceiveAsync is not driven); the branch is selected directly by the runtime key.
    /// </summary>
    private async Task<bool> RunBranchAsync(BranchSegment branch, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
    {
        if (branch.Router is null) return false;
        var routerOrder = NodeOrder(branch.Router);

        // Cross-chain redirect: target before the branch → skip the whole branch.
        if (redirectTarget is int t && routerOrder < t)
            return false;

        // Target is the router itself → re-route only, without recomputing.
        var reRouteOnly = redirectTarget is int t2 && t2 == routerOrder;
        if (!reRouteOnly)
            await DriveAsync(branch.Router, context, ct);

        if (branch.Router is ICompileTimeRouter router)
        {
            // Static: uses the compile-time locked key (the selected value at compile time); Dynamic: re-resolves at runtime.
            var key = branch.IsDynamic ? await router.ResolveRouteKey(context) : branch.CompileKey;
            context.BranchKey = key;
            var chosen = branch.Options.FirstOrDefault(o => o is not null && Equals(o.Key, key));
            if (chosen is null || chosen.IsTerminal)
            {
                context.Log($"Branch '{key}' has no downstream node; the run ends.");
                return true;
            }
            if (chosen.Graph is not null)
                return await RunGraphAsync(chosen.Graph, context, ct, redirectTarget);
        }
        return false;
    }

    /// <summary>
    /// Fan-out group: the branches run **concurrently**, as interleaved async operations rather than threads.
    /// Every branch starts on the caller's context and each await inside it yields to its siblings, so I/O-bound
    /// branches — a node invoking a process, say — overlap while the group stays on the host's
    /// SynchronizationContext. That last part is why this is not thread parallelism: a branch that burns CPU
    /// still occupies the thread in turn, and moving bodies to the pool would break the contract that components
    /// are UI-bound (the same contract <c>TrackedAIFunction</c> exists to keep).
    /// <para>
    /// Safety comes from each branch getting its own <see cref="BranchRuntimeContext"/>: payload, redirect
    /// request and log buffer are per branch, while identity, progress, the output registry and the shared
    /// variables stay on the host's single session. Every branch is a downstream of the SAME fan-out source, so
    /// each one starts from the source payload and can never see a sibling's output.
    /// </para>
    /// <para>
    /// Determinism, in branch order: when several branches ask to redirect the first in order wins while the rest
    /// are logged and ignored — first by *order*, not by wall clock, so a run stays reproducible. The payload left
    /// in the session is the last branch's, which is what the sequential loop used to leave there. A terminal
    /// branch hit inside any branch ends the whole run. One deliberate change: a branch that throws no longer
    /// aborts its siblings mid-flight — the exception surfaces once the group has finished.
    /// </para>
    /// <para>
    /// <b>Logs are not merged per branch.</b> Each branch writes straight into the session, so the lines read in
    /// the order they actually happened — which is what makes a file-backed <see cref="ILogWriter"/> and
    /// <see cref="IRuntimeContext.Logs"/> agree line for line. That ordering is the reason the sequence prefixes
    /// stay meaningful; do not reintroduce a per-branch buffer.
    /// </para>
    /// </summary>
    private async Task<bool> RunParallelAsync(ParallelSegment parallel, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
    {
        var sourceData = context.Data;   // the fan-out source's output, broadcast to every branch
        var branches = parallel.Branches;
        var count = branches.Count;

        // No branch, or one: nothing to interleave and nothing to merge, so keep the plain path.
        if (count == 0) return false;
        if (count == 1)
        {
            context.Data = sourceData;
            return await RunGraphAsync(branches[0], context, ct, redirectTarget);
        }

        // The cap is per group, and read off the host's session when that is the concrete type. Deliberately not
        // a member of IRuntimeContext: adding one would break every external implementation of the contract.
        var limit = (context as RuntimeContext)?.MaxParallelBranches;
        using var gate = limit is int n && n > 0 ? new SemaphoreSlim(n, n) : null;

        var branchContexts = new BranchRuntimeContext[count];
        var tasks = new Task<bool>[count];
        for (int i = 0; i < count; i++)
        {
            branchContexts[i] = new BranchRuntimeContext(context) { Data = sourceData };
            tasks[i] = RunOneBranchAsync(branches[i], branchContexts[i], ct, redirectTarget, gate);
        }

        var terminated = await Task.WhenAll(tasks);

        // No log merging: each branch writes straight into the session (see BranchRuntimeContext.Logs), so the
        // run's lines read in the order they happened even though the branches interleaved.

        int? winner = null;
        var winnerIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (branchContexts[i].PendingRedirectTarget is not int requested) continue;
            if (winner is null)
            {
                winner = requested;
                winnerIndex = i;
            }
            else
            {
                context.Log($"Redirect request to #{requested} from branch {i} was ignored; branch {winnerIndex} already asked for #{winner}.");
            }
        }
        if (winner is int target) context.PendingRedirectTarget = target;

        context.Data = branchContexts[count - 1].Data;   // what the sequential loop used to leave behind

        foreach (var ended in terminated)
            if (ended) return true;
        return false;
    }

    /// <summary>
    /// Runs one branch against its own <see cref="BranchRuntimeContext"/>, waiting on the group's concurrency
    /// gate first when the host set a cap (the cap bounds one fan-out, not the run).
    /// </summary>
    private async Task<bool> RunOneBranchAsync(
        CompiledGraph branch, BranchRuntimeContext branchContext, CancellationToken ct, int? redirectTarget,
        SemaphoreSlim? gate)
    {
        if (gate is not null) await gate.WaitAsync(ct);
        try
        {
            return await RunGraphAsync(branch, branchContext, ct, redirectTarget);
        }
        finally
        {
            gate?.Release();
        }
    }

    /// <summary>The node's compile-state Order (-1 when it does not implement ICompileTimeAware).</summary>
    private static int NodeOrder(IWorkflowNodeViewModel node)
        => (node as ICompileTimeAware)?.CompileContext?.Order ?? -1;

    /// <summary>
    /// Drives a single node: injects IRuntimeContext, executes the node through the unified data-flow
    /// entry <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/>, and writes the return value back to
    /// <see cref="IRuntimeContext.Data"/> for downstream chain passing.
    /// A node exception is pushed to the log via <see cref="IRuntimeContext.Error"/> and then rethrown
    /// (handled by RunExecuteAsync).
    /// </summary>
    private static async Task DriveAsync(IWorkflowNodeViewModel node, IRuntimeContext context, CancellationToken ct)
    {
        if (node is null || context is null) return;
        if (context.Target is { } target && ReferenceEquals(node, target))
            context.TargetReached = true;
        if (node is IRuntimeAware aware)
            aware.AttachRuntimeContext(context);
        // Execution status code = compile-time fixed number (stop nodes with Order = -1 are not driven, but keep the status code).
        var cc = (node as ICompileTimeAware)?.CompileContext;
        if (cc is not null)
            context.CurrentOrder = cc.Order;
        context.Log(node.GetType().Name);

        try
        {
            // Join injection: when compile-time registered inputs are plural (Count > 1) → the bare Data is
            // overridden with a read-only "source Node → output" dictionary; the node reads each upstream
            // result in ReceiveAsync via context.Data is IGroupData.
            if (cc?.InputNodes is { Count: > 1 } inputs)
                context.Data = new GroupData(context.CollectGroupedInputs(inputs));

            var result = await node.GetHelper().ReceiveAsync(context, ct);
            context.RegisterOutput(node, result);   // register the output after driving, for downstream join points to aggregate
            context.Data = result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Error(ex.Message);
            throw;
        }
    }
}
