using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Compiled execution engine: drives node execution along a compiled graph (<see cref="CompiledGraph"/>).
/// It does not rely on nodes broadcasting on their own — the engine "takes the data and drives the next node":
///  - ExecuteEntry: drives nodes one by one along a linear segment;
///  - BranchEntry: drives the router itself, then selects a branch via <see cref="ICompileTimeRouter.ResolveRouteKey"/> and drives the chosen subgraph;
///  - ParallelEntry: fan-out group, executes each branch in order (sequential order carries the "wait for all upstream" merge semantics).
/// When a node calls <see cref="IRuntimeContext.Error"/>/<see cref="IRuntimeContext.Warn"/> or throws inside ReceiveAsync,
/// it is treated as a redirect request: if the node implements <see cref="IRedirectable"/>, the engine re-runs the
/// whole graph with the returned compile state (CompileContext.Order), skipping nodes before the target (possibly cross-chain);
/// when the target is a Router it only re-routes without recomputing.
/// If the node does not implement IRedirectable, the whole flow ends with the standard -1 status.
/// Before each drive the engine injects <see cref="IRuntimeContext"/> into <see cref="IRuntimeAware"/> nodes.
/// </summary>
public sealed class CompilerEngine
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
                case ExecuteEntry exec:
                    terminated = await RunExecuteAsync(exec, context, ct, redirectTarget);
                    break;
                case BranchEntry branch:
                    terminated = await RunBranchAsync(branch, context, ct, redirectTarget);
                    break;
                case ParallelEntry parallel:
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
    private async Task<bool> RunExecuteAsync(ExecuteEntry exec, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
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
    private async Task<bool> RunBranchAsync(BranchEntry branch, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
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
    /// Fan-out group: executes all branch subgraphs in order — the order carries the "wait for all
    /// upstreams to arrive" merge semantics (the shared IRuntimeContext blackboard is not thread-safe,
    /// so there is no true parallelism). Every branch is a downstream of the SAME fan-out source, so the
    /// source payload is restored before each branch — otherwise the previous branch's output would leak
    /// into the next branch as its input. A terminal branch hit inside any branch ends the whole run.
    /// </summary>
    private async Task<bool> RunParallelAsync(ParallelEntry parallel, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
    {
        var sourceData = context.Data;   // the fan-out source's output, broadcast to every branch
        foreach (var branch in parallel.Branches)
        {
            context.Data = sourceData;   // each branch reads the same source payload, not the previous branch's output
            if (await RunGraphAsync(branch, context, ct, redirectTarget)) return true;
        }
        return false;
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
