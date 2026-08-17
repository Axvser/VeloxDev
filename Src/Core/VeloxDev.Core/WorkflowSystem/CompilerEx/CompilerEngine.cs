using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译执行引擎：按编译图（<see cref="CompiledGraph"/>）驱动节点执行。
/// 不依赖节点自主 Broadcast —— 全是引擎"拿数据 → 驱动下一个节点"：
///  - ExecuteEntry：线性段逐个驱动节点；
///  - BranchEntry：先驱动 router 自身，再经 <see cref="ICompileTimeRouter.ResolveRouteKey"/> 选分支，驱动选中分支子图；
///  - ParallelEntry：扇出组，顺序执行各分支（顺序即"等待所有上游"的汇聚语义）。
/// 节点在 ReceiveAsync 中调用 <see cref="IRuntimeContext.Error"/>/<see cref="IRuntimeContext.Warn"/> 或抛异常
/// 都视为请求重定向：若节点实现 <see cref="IRedirectable"/>，引擎按它返回的编译状态（CompileContext.Order）
/// 带该目标**重跑整张图**（跳过目标之前的节点，可跨链）；目标是 Router 时只重新路由、不重新计算。
/// 若节点未实现 IRedirectable，则整个流程结束，状态标记为标准 -1。
/// 每次驱动前给 <see cref="IRuntimeAware"/> 节点注入 <see cref="IRuntimeContext"/>。
/// </summary>
public sealed class CompilerEngine
{
    public async Task RunAsync(CompiledGraph graph, IRuntimeContext context, CancellationToken ct)
    {
        if (graph is null || context is null) return;
        const int MaxRedirects = 50;
        context.IsRunning = true;
        context.Status = "Running";
        int? redirectTarget = null;
        var redirects = 0;
        try
        {
            // 重定向统一为「带目标 Order 重跑整张图」：跳过目标之前的节点（可跨链）。
            while (true)
            {
                context.Attempt = redirects + 1;   // 图重跑计数（每次回退 +1）
                context.PendingRedirectTarget = null;
                var terminated = await RunGraphAsync(graph, context, ct, redirectTarget);
                if (!terminated && context.PendingRedirectTarget is { } next)
                {
                    redirects++;
                    if (redirects > MaxRedirects)
                    {
                        context.Error($"回退超过 {MaxRedirects} 次，放弃。");
                        throw new InvalidOperationException($"回退超过 {MaxRedirects} 次，放弃。");
                    }
                    context.Log($"→ 回退到编译状态 #{next}（跳过其前节点，重新执行）。");
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

    /// <summary>驱动一张图的所有条目。返回 true 表示运行到此结束（终端分支或报错终止）。</summary>
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
    /// 驱动一条线性链。跨链回退时跳过 Order &lt; 目标 的节点；节点报错（Error/Warn/抛异常）则视为请求重定向：
    /// 有 <see cref="IRedirectable"/> → 由其返回回退目标（可跨链），置 <see cref="IRuntimeContext.PendingRedirectTarget"/>
    /// 由 RunAsync 带目标重跑整张图；无 → 流程结束、状态 -1。返回 true 表示流程提前结束。
    /// </summary>
    private async Task<bool> RunExecuteAsync(ExecuteEntry exec, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
    {
        for (int i = 0; i < exec.Nodes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var node = exec.Nodes[i];
            if (node is null) continue;
            var order = NodeOrder(node);

            // 跨链回退的跳过语义：目标之前的节点不驱动。
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
                throw;   // 取消不是重定向
            }
            catch (Exception)
            {
                // 节点 ReceiveAsync 抛异常 → DriveAsync 已记 context.Error 并置 RedirectRequested；
                // 捕获后走下方「重定向或结束流程」逻辑，而不是中止整张图。
            }

            if (!context.RedirectRequested) continue;

            // 节点报错但未实现 IRedirectable → 整个流程结束，状态置 -1。
            if (node is not IRedirectable redirectable)
            {
                context.CurrentOrder = -1;
                context.EndedWithError = true;
                context.Error("节点报告错误但未实现 IRedirectable，流程结束（状态 -1）。");
                return true;
            }

            // 有 IRedirectable → 由其接口决定回退目标（可跨链）。仅接受前驱状态（Order < 当前）。
            var target = await redirectable.ResolveRedirectAsync(context, ct);
            if (target is { } targetOrder && targetOrder < order)
            {
                context.PendingRedirectTarget = targetOrder;
            }
            else
            {
                context.Log($"→ 回退目标 #{target} 非前驱或无效，忽略。");
            }
        }
        return false;
    }

    /// <summary>
    /// 驱动分支。跨链回退时：目标在分支之前 → 跳过整个分支；目标恰好是 router → **只重新路由**，
    /// 不重新计算（不驱动 router 的 ReceiveAsync），直接按运行期键选分支。
    /// </summary>
    private async Task<bool> RunBranchAsync(BranchEntry branch, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
    {
        if (branch.Router is null) return false;
        var routerOrder = NodeOrder(branch.Router);

        // 跨链回退：目标在分支之前 → 跳过整个分支。
        if (redirectTarget is int t && routerOrder < t)
            return false;

        // 目标恰好是 router → 只重新路由、不重新计算。
        var reRouteOnly = redirectTarget is int t2 && t2 == routerOrder;
        if (!reRouteOnly)
            await DriveAsync(branch.Router, context, ct);

        if (branch.Router is ICompileTimeRouter router)
        {
            // Static：以编译期锁定的 key 为准（编译瞬间的选中值）；Dynamic：运行期重解析。
            var key = branch.IsDynamic ? await router.ResolveRouteKey(context) : branch.CompileKey;
            context.BranchKey = key;
            var chosen = branch.Options.FirstOrDefault(o => o is not null && Equals(o.Key, key));
            if (chosen is null || chosen.IsTerminal)
            {
                context.Log($"→ 分支 '{key}' 无下游节点，运行结束。");
                return true;
            }
            if (!chosen.IsSkipped && chosen.Graph is not null)
                return await RunGraphAsync(chosen.Graph, context, ct, redirectTarget);
        }
        return false;
    }

    /// <summary>
    /// 扇出组：顺序执行所有分支子图——顺序即"等待所有上游到达"的汇聚语义
    /// （共享 IRuntimeContext 黑板非线程安全，不做真并行）。任一分支内部命中终端分支 → 终止整个运行。
    /// </summary>
    private async Task<bool> RunParallelAsync(ParallelEntry parallel, IRuntimeContext context, CancellationToken ct, int? redirectTarget)
    {
        foreach (var branch in parallel.Branches)
        {
            if (await RunGraphAsync(branch, context, ct, redirectTarget)) return true;
        }
        return false;
    }

    /// <summary>节点的编译状态 Order（未实现 ICompileTimeAware 视为 -1）。</summary>
    private static int NodeOrder(IWorkflowNodeViewModel node)
        => (node as ICompileTimeAware)?.CompileContext?.Order ?? -1;

    /// <summary>
    /// 驱动单个节点：注入 IRuntimeContext，经统一数据流入口
    /// <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/> 执行节点并拿到返回值，
    /// 写回 <see cref="IRuntimeContext.Data"/> 供下游链式传递。
    /// 节点异常经 <see cref="IRuntimeContext.Error"/> 推送到日志后向上抛出（由 RunExecuteAsync 捕获处理）。
    /// </summary>
    private static async Task DriveAsync(IWorkflowNodeViewModel node, IRuntimeContext context, CancellationToken ct)
    {
        if (node is null || context is null) return;
        if (node is IRuntimeAware aware)
            aware.AttachRuntimeContext(context);
        // 执行状态码 = 编译期固定编号（Order = -1 的停止节点不驱动，但保持状态码）。
        if (node is ICompileTimeAware compileAware && compileAware.CompileContext is { } cc)
            context.CurrentOrder = cc.Order;
        context.Log($"→ {node.GetType().Name}");

        try
        {
            var result = await node.GetHelper().ReceiveAsync(context, ct);
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
