using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译执行引擎：按编译图（<see cref="CompiledGraph"/>）驱动节点执行。
/// 不依赖节点自主 Broadcast —— 全是引擎"拿数据 → 驱动下一个节点"：
///  - ExecuteEntry：线性段逐个驱动节点；
///  - BranchEntry：先驱动 router 自身，再经 <see cref="ICompileTimeRouter.ResolveRouteKey"/> 选分支，驱动选中分支子图；
///  - RetryEntry：失败重跑环路体直到超过 MaxRetries（v1 简化为尝试上限次）。
/// 每次驱动前给 <see cref="IRuntimeAware"/> 节点注入 <see cref="RuntimeContext"/>。
/// </summary>
public sealed class CompilerEngine
{
    public async Task RunAsync(CompiledGraph graph, RuntimeContext context, CancellationToken ct)
    {
        if (graph is null || context is null) return;
        context.IsRunning = true;
        context.Status = "Running";
        try
        {
            await RunGraphAsync(graph, context, ct);
            context.Status = "Completed";
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

    private async Task RunGraphAsync(CompiledGraph? graph, RuntimeContext? context, CancellationToken ct)
    {
        if (graph is null || context is null) return;
        foreach (var entry in graph.Entries)
        {
            ct.ThrowIfCancellationRequested();
            context.CurrentEntry = entry;
            switch (entry)
            {
                case ExecuteEntry exec:
                    await RunExecuteAsync(exec, context, ct);
                    break;
                case BranchEntry branch:
                    await RunBranchAsync(branch, context, ct);
                    break;
                case RetryEntry retry:
                    await RunRetryAsync(retry, context, ct);
                    break;
            }
        }
    }

    private static async Task RunExecuteAsync(ExecuteEntry exec, RuntimeContext context, CancellationToken ct)
    {
        for (int i = 0; i < exec.Nodes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var node = exec.Nodes[i];
            if (node is null) continue;
            context.NodeIndex = i;
            await DriveAsync(node, context, ct);
        }
    }

    private async Task RunBranchAsync(BranchEntry branch, RuntimeContext context, CancellationToken ct)
    {
        if (branch.Router is null) return;
        // router 自身先执行（记录路由等）
        await DriveAsync(branch.Router, context, ct);

        if (branch.Router is ICompileTimeRouter router)
        {
            var key = await router.ResolveRouteKey(context);
            context.BranchKey = key;
            var chosen = branch.Options.FirstOrDefault(o => o is not null && Equals(o.Key, key));
            if (chosen is { Graph: not null } && !chosen.IsSkipped)
                await RunGraphAsync(chosen.Graph, context, ct);
        }
    }

    private async Task RunRetryAsync(RetryEntry retry, RuntimeContext context, CancellationToken ct)
    {
        var max = Math.Max(1, retry.MaxRetries);
        for (context.Attempt = 1; context.Attempt <= max; context.Attempt++)
        {
            try
            {
                await RunGraphAsync(retry.Body, context, ct);
                return;   // 环路体成功 → 退出
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 失败 → 重跑 */ }
        }
    }

    /// <summary>
    /// 驱动单个节点：注入 RuntimeContext，执行其 WorkCommand 并等待真正完成。
    /// 节点异常经 <see cref="RuntimeContext.Error"/> 推送到日志后向上抛出（RetryEntry 可据此重试）。
    /// </summary>
    private static async Task DriveAsync(IWorkflowNodeViewModel node, RuntimeContext context, CancellationToken ct)
    {
        if (node is null || context is null) return;
        if (node is IRuntimeAware aware)
            aware.AttachRuntimeContext(context);
        // 执行状态码 = 编译期固定编号（Order = -1 的停止节点不驱动，但保持状态码）。
        if (node is ICompileTimeAware compileAware && compileAware.CompileContext is { } cc)
            context.CurrentOrder = cc.Order;
        context.Log($"→ {node.GetType().Name}");

        Exception? failure = null;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            void OnExited(CommandEventArgs e) => tcs.TrySetResult(null);
            void OnFailed(CommandEventArgs e) { failure = e.Exception; tcs.TrySetResult(null); }
            node.WorkCommand.Exited += OnExited;
            node.WorkCommand.Failed += OnFailed;
            try
            {
                await node.WorkCommand.ExecuteAsync(context);
                await tcs.Task;
            }
            finally
            {
                node.WorkCommand.Exited -= OnExited;
                node.WorkCommand.Failed -= OnFailed;
            }
        }

        if (failure is not null)
        {
            context.Error(failure.Message);
            throw failure;
        }
    }
}
