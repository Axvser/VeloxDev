using System.Diagnostics;
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
/// <para>
/// <b>Two levels, two outcomes.</b> <see cref="IRuntimeContext.Warn"/> is a note: the line is written and the run
/// carries on with whatever the node returned. <see cref="IRuntimeContext.Error"/>, and an exception a node did not
/// catch, are a stop: the line is written, that drive counts as having produced a null, and — unless the node
/// implements <see cref="IRedirectable"/>, which then gets to place the run instead — the whole flow ends with
/// status -1. A router or a redirect contract that throws ends it too, for the same reason: there is no answer left
/// to give.
/// </para>
/// Before each drive the engine injects <see cref="IRuntimeContext"/> into <see cref="IRuntimeAware"/> nodes.
/// <para>
/// Five capabilities are optional and read off the host's <see cref="RuntimeContext"/> — the pause gate, the
/// observer, the retry policy, the error sink and the compensator. Every one of them is a bypass: with none
/// configured, the run behaves exactly as it did before they existed, down to the log lines and the number of
/// drives. They are resolved through <c>Session</c>, so they also work inside a fan-out.
/// </para>
/// </summary>
public sealed class RuntimeEngine
{
    public async Task RunAsync(CompiledGraph graph, IRuntimeContext context, CancellationToken ct)
    {
        if (graph is null || context is null) return;
        const int MaxRedirects = 50;
        var session = Session(context);
        var startedAt = Stopwatch.GetTimestamp();
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
                if (redirects == 0)
                    await ObserveAsync(session, context, ExecutionObservationKind.RunStarted, null, null, TimeSpan.Zero, ct);
                context.PendingRedirectTarget = null;
                context.ActiveRedirectTarget = redirectTarget;   // null on the first pass; output collection uses it to tell contract-preserved prefix from stale branches
                var terminated = await RunGraphAsync(graph, context, ct, redirectTarget);
                if (!terminated && context.PendingRedirectTarget is { } next)
                {
                    redirects++;
                    if (redirects > MaxRedirects)
                    {
                        // 先结束再抛：这条路上 Status 原本停在 "Running"，异常一路穿出去时会话还在说「在跑」。
                        context.CurrentOrder = -1;
                        context.EndedWithError = true;
                        context.Status = "Stopped";
                        await ReportErrorAsync(session, context, ExecutionFailurePhase.Run, null,
                            $"Redirected more than {MaxRedirects} times. Aborting.", null, ct);
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
        catch (OperationCanceledException ex)
        {
            context.Status = "Stopped";
            // 只报给 sink，不写日志：宿主自己停的运行不是失败，加一行 [Error] 会让以后读 Logs 的人以为出过错。
            await NotifyErrorAsync(session, context, ExecutionFailurePhase.Run, null, "The run was cancelled.", ex, CancellationToken.None);
        }
        finally
        {
            context.IsRunning = false;
            var outcome = OutcomeOf(context);
            if (session is not null) session.Outcome = outcome;   // 只有具体类装得下；宿主自带的会话没地方放
            await CompensateAsync(session, context, outcome);
            // 放在最后：宿主在 RunEnded 里收尾时，补偿已经走完。不传运行令牌 —— 取消之后它已取消，观察者会拒掉这条唯一能收束的观察。
            await ObserveAsync(session, context, ExecutionObservationKind.RunEnded, null, null, ElapsedSince(startedAt), CancellationToken.None);
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
        var session = Session(context);
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
            context.RedirectRequested = false;   // 一并清掉 ReportedLevel（派生属性）
            ExecutionReportLevel? reported;
            try
            {
                await DriveAsync(node, context, ct);
                reported = ReportedLevel(context, session);
            }
            catch (OperationCanceledException)
            {
                throw;   // cancellation is not a report
            }
            catch (Exception)
            {
                // DriveAsync 已在 catch 里记过 Error（也就是 Error 档）、问过重试策略，并把 Data 置空。
                reported = ExecutionReportLevel.Error;
            }

            // 只是提醒：值照常传下去，运行继续，连 IRedirectable 都不问 —— 没出问题就没有要改的路线。
            if (reported is null or ExecutionReportLevel.Warning) continue;

            // 报的是错但没人能处理它 ⇒ 整轮就此结束，与从前一样（CurrentOrder = -1、EndedWithError）。
            if (node is not IRedirectable redirectable)
            {
                context.CurrentOrder = -1;
                context.EndedWithError = true;
                await ReportErrorAsync(session, context, ExecutionFailurePhase.Node, node,
                    "Node reported an error but does not implement IRedirectable; the flow ends (status -1).", null, ct);
                return true;
            }

            // With IRedirectable → its interface decides the redirect target (possibly cross-chain).
            // Only a predecessor state (Order < current) is accepted.
            int? target;
            try
            {
                target = await redirectable.ResolveRedirectAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // IRedirectable 是宿主实现的契约，它的 bug 与节点体抛异常同一种失败。原先没有守卫，异常会穿出
                // RunAsync 并把 Status 停在 "Running" —— 明明什么都没在跑，会话却说还在跑。
                context.CurrentOrder = -1;
                context.EndedWithError = true;
                await ReportErrorAsync(session, context, ExecutionFailurePhase.Redirect, node, ex.Message, ex, ct);
                return true;
            }

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
        var session = Session(context);
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
            object? key;
            try
            {
                // 静态：用编译期锁定的键（编译那一刻的选中值）；动态：运行期重新解析。
                key = branch.IsDynamic ? await router.ResolveRouteKey(context) : branch.CompileKey;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 另一处宿主钩子（见 RunExecuteAsync 的重定向解析）：原来同样会穿出 RunAsync 并把 Status 停在
                // "Running"。没有键就没有可走的路，所以在这里结束，而不是随便挑一条。
                context.CurrentOrder = -1;
                context.EndedWithError = true;
                await ReportErrorAsync(session, context, ExecutionFailurePhase.Router, branch.Router, ex.Message, ex, ct);
                return true;
            }

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
            tasks[i] = RunOneBranchAsync(branches[i], branchContexts[i], i, ct, redirectTarget, gate);
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
        CompiledGraph branch, BranchRuntimeContext branchContext, int index, CancellationToken ct, int? redirectTarget,
        SemaphoreSlim? gate)
    {
        if (gate is not null) await gate.WaitAsync(ct);
        try
        {
            await ObserveAsync(Session(branchContext), branchContext, ExecutionObservationKind.BranchStarted, null, $"branch {index}", TimeSpan.Zero, ct);
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

    // 驱动一个节点：注入 IRuntimeContext、经 ReceiveAsync 执行、把返回值写回 Data 供下游链式传递。
    // 抛异常照旧记 Error 再抛（RunExecuteAsync 决定谁来安排下一步）—— 除非配了重试策略并愿意再给一次机会。
    // 报了错或抛异常的驱动记成「返回 null」交给下游；只是警告的，值照传（提醒不该吃掉结果）。
    private static async Task DriveAsync(IWorkflowNodeViewModel node, IRuntimeContext context, CancellationToken ct)
    {
        if (node is null || context is null) return;
        var session = Session(context);

        // 暂停点：每个节点驱动前问一次，节点体内绝不打断（与工具层「半个变更不允许中途丢弃」同一条规矩）。
        // 只在门真的关上时才写 Status —— 开着的门不该让会话每过一个节点闪一次 "Paused"。
        if (session?.ExecutionGate is { } gate)
        {
            var wait = gate.WaitAsync(ct);
            if (wait.IsCompleted)
            {
                await wait;   // 门开着：不产生 await 让位，也不动 Status
            }
            else
            {
                context.Status = "Paused";
                try { await wait; }
                finally { context.Status = "Running"; }
            }
        }

        if (context.Target is { } target && ReferenceEquals(node, target))
            context.TargetReached = true;
        // Execution status code = compile-time fixed number (stop nodes with Order = -1 are not driven, but keep the status code).
        var cc = (node as ICompileTimeAware)?.CompileContext;
        if (cc is not null)
            context.CurrentOrder = cc.Order;
        context.Log(node.GetType().Name);

        // 记下这次驱动的是哪个节点：节点经 context.ErrorAsync/WarnAsync 报错时要带进结构化记录。
        // 扇出里必须写在门面上 —— 写在会话上会被交错的分支互相覆盖。
        if (context is BranchRuntimeContext branch) branch.CurrentNode = node;
        else if (session is not null) session.CurrentNode = node;

        // 节点即将收到的载荷。留一份是为了重试从同一个输入开始，而不是接着失败那次留下的半成品。
        var input = context.Data;
        var failures = 0;
        while (true)
        {
            var startedAt = Stopwatch.GetTimestamp();
            await ObserveAsync(session, context, ExecutionObservationKind.NodeStarted, node, null, TimeSpan.Zero, ct);

            try
            {
                // 注入放在失败纪律之内，而不是之前：宿主实现的 AttachRuntimeContext 抛异常时会落进
                // RunExecuteAsync 的空 catch —— 节点被无声跳过，没有日志、没有重定向，状态看着还正常。
                if (node is IRuntimeAware aware)
                    aware.AttachRuntimeContext(context);

                // Join injection: when compile-time registered inputs are plural (Count > 1) → the bare Data is
                // overridden with a read-only "source Node → output" dictionary; the node reads each upstream
                // result in ReceiveAsync via context.Data is IGroupData.
                if (cc?.InputNodes is { Count: > 1 } inputs)
                    context.Data = new GroupData(context.CollectGroupedInputs(inputs));

                var result = await node.GetHelper().ReceiveAsync(context, ct);

                // 报了错 ⇒ 这次驱动视为返回 null（日志已经忠实记下）；只是警告 ⇒ 值照常传下去，提醒不该吃掉结果。
                if (ReportedLevel(context, session) == ExecutionReportLevel.Error) result = null;

                context.RegisterOutput(node, result);   // register the output after driving, for downstream join points to aggregate
                context.Data = result;
                await ObserveAsync(session, context, ExecutionObservationKind.NodeSucceeded, node, null, ElapsedSince(startedAt), ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;   // 取消从不重试
            }
            catch (Exception ex)
            {
                failures++;
                var elapsed = ElapsedSince(startedAt);
                await ObserveAsync(session, context, ExecutionObservationKind.NodeFailed, node, ex.Message, elapsed, ct);

                var delay = await NextRetryAsync(session, context, node, ex, failures, elapsed, ct);
                if (delay is { } wait)
                {
                    // 重试不是新一轮：Attempt 数的是「过图的趟数」，同时是产物表的戳，重试时动它会让汇合聚合跟着变。
                    context.Log($"[Retry {failures}] {node.GetType().Name}: {ex.Message}");
                    await ObserveAsync(session, context, ExecutionObservationKind.NodeRetried, node, failures.ToString(), TimeSpan.Zero, ct);
                    await Task.Delay(wait, ct);
                    context.Data = input;
                    continue;
                }

                await ReportErrorAsync(session, context, ExecutionFailurePhase.Node, node, ex.Message, ex, ct);
                context.RegisterOutput(node, null);   // 同上：抛异常的这次驱动同样记为「返回 null」
                context.Data = null;
                throw;
            }
        }
    }

    // 问一次重试策略：失败的那次还能不能再来一次。返回等待时长 = 再来，null = 交回引擎原有路径。
    // 只有抛出的异常会被问，而且节点自己请求过重定向时不问 —— Error()/Warn() 是节点选择的控制流，不是待重试的失败。
    // 策略自己抛异常当作「不再试」：宿主的 bug 不该顶替节点本来的失败。
    private static async Task<TimeSpan?> NextRetryAsync(
        RuntimeContext? session, IRuntimeContext context, IWorkflowNodeViewModel node, Exception error, int failures,
        TimeSpan elapsed, CancellationToken ct)
    {
        if (context.RedirectRequested) return null;
        if (session?.RetryPolicy is not { } policy) return null;

        try
        {
            return await policy.NextRetryAsync(new NodeFailure(node, error, failures, elapsed), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Log($"[Retry] {node.GetType().Name} was not retried: the policy failed to decide ({ex.Message}).");
            return null;
        }
    }

    // 按引擎原有方式记一次失败（一行日志），并把同一条失败交给可选的 sink。
    private static async Task ReportErrorAsync(
        RuntimeContext? session, IRuntimeContext context, ExecutionFailurePhase phase, IWorkflowNodeViewModel? node,
        string message, Exception? error, CancellationToken ct)
    {
        context.Error(message);
        await NotifyErrorAsync(session, context, phase, node, message, error, ct);
    }

    // 只交给 sink、不写日志：日志里已经有这条失败，或者本就不该有（宿主主动取消不是错误）。
    private static async Task NotifyErrorAsync(
        RuntimeContext? session, IRuntimeContext context, ExecutionFailurePhase phase, IWorkflowNodeViewModel? node,
        string message, Exception? error, CancellationToken ct)
    {
        if (session?.ErrorSink is not { } sink) return;
        var record = new ExecutionError(phase, node, message, error, context.Attempt, node is null ? -1 : NodeOrder(node));
        try
        {
            await sink.OnErrorAsync(record, ct);
        }
        catch (Exception ex)
        {
            context.Log($"[ErrorSink] the failure was not recorded: {ex.Message}");   // 报告失败不能自己添一条失败
        }
    }

    // 把这一轮成功驱动的节点按逆序交给可选的补偿器 —— 前提是运行以 Failed/Cancelled 收尾。
    // 引擎自己什么都不回滚：一个节点的副作用是什么、哪些可逆，只有宿主知道。
    private static async Task CompensateAsync(RuntimeContext? session, IRuntimeContext context, RunOutcome outcome)
    {
        if (session?.Compensation is not { } compensation) return;
        if (outcome is not (RunOutcome.Failed or RunOutcome.Cancelled)) return;

        var completed = session.CompletedThisRun;
        for (var i = completed.Count - 1; i >= 0; i--)
        {
            var (node, output) = completed[i];
            try
            {
                // 不传运行令牌：取消之后它已取消，而清理恰恰是仍然必须发生的那件事。
                await compensation.CompensateAsync(new NodeCompensation(node, output, NodeOrder(node)), CancellationToken.None);
            }
            catch (Exception ex)
            {
                // 尽力而为：一个节点撤不回来，既不能盖住最初那次失败，也不能连累后面还没撤的节点。
                context.Log($"[Compensation] {node.GetType().Name} was not compensated: {ex.Message}");
            }
        }
    }

    // 发一条观察给可选的观察者，它拿它做什么都不影响运行：抛异常只换回一行日志。
    // 与日志写入器的契约正好相反（那边失败本身就是证据，必须上报），观察不是证据。
    private static async Task ObserveAsync(
        RuntimeContext? session, IRuntimeContext context, ExecutionObservationKind kind, IWorkflowNodeViewModel? node,
        string? detail, TimeSpan elapsed, CancellationToken ct)
    {
        if (session?.Observer is not { } observer) return;
        var observation = new ExecutionObservation(kind, node, detail, context.Attempt, elapsed);
        try
        {
            await observer.OnObservedAsync(observation, ct);
        }
        catch (Exception ex)
        {
            context.Log($"[Observer] {kind} was not observed: {ex.Message}");
        }
    }

    // 运行结局：把 Status 与 EndedWithError 这两个老成员读准。
    private static RunOutcome OutcomeOf(IRuntimeContext context)
        => context.Status switch
        {
            "Completed" => RunOutcome.Completed,
            // "Stopped" 一个词担着两种收尾，靠 EndedWithError 分开。
            "Stopped" => context.EndedWithError ? RunOutcome.Failed : RunOutcome.Cancelled,
            // 还在跑，或者根本没跑起来 —— 异常穿出 RunAsync 时 Status 就停在这个分支上。
            _ => RunOutcome.Unknown,
        };

    // 这次驱动报的级别：门面自带一份（分支私有，与 Data 同理），不在门面上时读会话。
    private static ExecutionReportLevel? ReportedLevel(IRuntimeContext context, RuntimeContext? session)
        => context is BranchRuntimeContext branch ? branch.ReportedLevel : session?.ReportedLevel;

    // 引擎手里那个上下文背后的宿主会话。扇出分支拿到的是 BranchRuntimeContext 门面，
    // 能力要透过它去取，否则门/观察者恰好在宽图最花时间的地方静默失效。
    // 宿主自带 IRuntimeContext 实现时返回 null —— 与 MaxParallelBranches 同样的取舍：拿不到这些可选能力。
    private static RuntimeContext? Session(IRuntimeContext context)
        => context switch
        {
            RuntimeContext session => session,
            BranchRuntimeContext branch => branch.Session as RuntimeContext,
            _ => null,
        };

    // 距某个 Stopwatch.GetTimestamp 读数过了多久，供观察事件用。
    private static TimeSpan ElapsedSince(long timestamp)
        => TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - timestamp) / (double)Stopwatch.Frequency);
}
