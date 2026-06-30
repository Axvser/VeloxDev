using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// The compiled output: a read-only ordered list of items.
/// Call <see cref="ExecuteAsync"/> to run them in sequence.
///
/// Execution model (compiled mode):
/// The compiler's <see cref="CompilationResult"/> owns the execution order.
/// Helper classes (<c>WorkAsync</c>) should NOT self-propagate via broadcast
/// when running inside a compiled chain — instead they set <see cref="IWorkflowNodeViewModel.WorkResult"/>
/// and the compiler forwards it to the next item automatically.
/// </summary>
public sealed class CompilationResult
{
    private readonly IReadOnlyList<CompiledItem> _items;

    /// <summary>The ordered list of compiled items.</summary>
    public IReadOnlyList<CompiledItem> Items => _items;

    /// <summary>The traversal mode used during compilation.</summary>
    public CompileMode Mode { get; }

    /// <summary>The edge traversal direction used during compilation.</summary>
    public CompileDirection Direction { get; }

    /// <summary>The traversal scope used during compilation.</summary>
    public CompileScope Scope { get; }

    /// <summary>Whether a cycle was detected in the graph.</summary>
    public bool HasCycle { get; }

    /// <summary>The strategy used for handling cycles.</summary>
    public CycleHandling CycleHandling { get; }

    internal CompilationResult(IReadOnlyList<CompiledItem> items, CompileMode mode,
        CompileDirection direction, CompileScope scope, bool hasCycle,
        CycleHandling cycleHandling = CycleHandling.Throw)
    {
        _items = items;
        Mode = mode;
        Direction = direction;
        Scope = scope;
        HasCycle = hasCycle;
        CycleHandling = cycleHandling;
    }

    /// <summary>
    /// Execute all items in deterministic order.
    ///
    /// For each item:
    ///   1. Subscribe to WorkCommand.Failed for error capture.
    ///   2. Notify <see cref="ICompileTimeSink"/> (BeforeExecute).
    ///   3. Execute WorkCommand and wait for actual completion via Exited event.
    ///   4. Read <see cref="IWorkflowNodeViewModel.WorkResult"/> and forward it to the next item.
    ///   5. On failure: notify OnError, then if <see cref="CompiledItem.ErrorRedirectId"/> is set,
    ///      execute the target node asynchronously and chain its WorkResult.
    ///   6. Notify <see cref="ICompileTimeSink"/> (AfterExecute).
    ///
    /// Because <see cref="IVeloxCommand.ExecuteAsync"/> dispatches the command
    /// asynchronously (fire-and-forget internally), this method hooks the
    /// <see cref="IVeloxCommand.Exited"/> event to wait for genuine completion.
    ///
    /// The parameter is passed directly (NOT wrapped in <see cref="WorkContext"/>)
    /// so that downstream helpers can access the original object — e.g.
    /// <c>NetworkFlowContext.From(parameter)</c> works without unwrapping.
    ///
    /// Note: graph-level cycles do not produce runtime loops because each compiled
    /// item appears exactly once in the ordered list. <see cref="CycleHandling.Allow"/>
    /// merely preserves cycle metadata for informational purposes.
    ///
    /// <returns>The last node's result, or the initial parameter if no node produced a result.</returns>
    /// </summary>
    public async Task<object?> ExecuteAsync(object? parameter = null, CancellationToken ct = default)
    {
        object? currentParam = parameter;
        var skippedItems = new HashSet<int>(); // 未选中分支的独占项 ID
        var totalCount = _items.Count;

        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();

            // 跳过被路由分支排除的项
            if (skippedItems.Contains(item.Id))
            {
                NotifyExecutionSink(item, currentParam, ExecutionEvent.BeforeExecute, totalCount);
                NotifyExecutionSink(item, currentParam, ExecutionEvent.AfterExecute, totalCount);
                continue;
            }

            // 订阅 Failed 事件（捕获异常，不在此同步处理）
            item.SubscribeError();

            // 通知：执行前
            NotifyExecutionSink(item, currentParam, ExecutionEvent.BeforeExecute, totalCount);

            try
            {
                // 执行并等待真正完成
                await ExecuteItemAsync(item, currentParam, ct);
            }
            catch (OperationCanceledException)
            {
                // 取消信号：立即中止整条执行链，不再处理后续节点
                item.UnsubscribeError();
                throw;
            }
            catch (Exception ex)
            {
                // 执行中抛出的其他异常转储到 FailureException，统一走后面的错误处理路径
                item.FailureException = ex;
            }

            // 错误处理：如果节点失败，检查是否需要重定向
            if (item.FailureException is not null)
            {
                NotifyExecutionSink(item, currentParam, ExecutionEvent.OnError, totalCount);

                if (item.ErrorRedirectId.HasValue)
                {
                    var target = _items.FirstOrDefault(i => i.Id == item.ErrorRedirectId.Value);
                    if (target is not null)
                    {
                        var errorCtx = new ErrorContext(
                            item.Id, item.FailureException, currentParam, currentParam);
                        // 异步执行重定向目标
                        await ExecuteItemAsync(target, new WorkContext(errorCtx), ct);
                        // 链入重定向目标的输出
                        if (target.Node.WorkResult is not null)
                            currentParam = target.Node.WorkResult;
                        // 标记重定向目标，防止在主循环中被重复执行
                        if (!skippedItems.Contains(target.Id))
                            skippedItems.Add(target.Id);
                    }
                }
            }
            else
            {
                // 结果链传递：将节点输出传给下一个
                if (item.Node.WorkResult is not null)
                    currentParam = item.Node.WorkResult;
            }

            // 路由分支排除：如果当前节点是路由器，根据选中的 key 跳过未选分支的独占项
            if (item.FailureException is null &&
                item.BranchExclusiveItems is not null &&
                item.Node is ICompileTimeRouter router)
            {
                var chosenKey = router.GetCurrentRouteKey();
                foreach (var kv in item.BranchExclusiveItems)
                {
                    // 未选中的分支 → 其独占项全部跳过
                    if (!Equals(kv.Key, chosenKey))
                    {
                        foreach (var skipId in kv.Value)
                            skippedItems.Add(skipId);
                    }
                }
            }

            NotifyExecutionSink(item, currentParam, ExecutionEvent.AfterExecute, totalCount);
            item.UnsubscribeError();
        }

        // 通知：全链执行完毕
        NotifyAllExecutionSinks(currentParam, ExecutionEvent.OnCompleted, totalCount);

        return currentParam;
    }

    /// <summary>
    /// 执行单个 CompiledItem 并等待其真正完成。
    ///
    /// VeloxCommand.ExecuteAsync 内部使用 fire-and-forget 调度命令，
    /// 返回的 Task 在调度完成后即完成，不等待命令实际执行完毕。
    /// 此方法通过订阅 Exited 事件来等待真正的执行结束。
    /// </summary>
    private static async Task ExecuteItemAsync(CompiledItem item, object? parameter, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (ct.Register(() => tcs.TrySetCanceled()))
        {
            void OnExited(CommandEventArgs e) => tcs.TrySetResult(null);

            item.Node.WorkCommand.Exited += OnExited;
            try
            {
                // 调度执行（fire-and-forget）
                await item.Node.WorkCommand.ExecuteAsync(parameter).ConfigureAwait(false);
                // 等待 Exited 事件确认执行完毕
                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                item.Node.WorkCommand.Exited -= OnExited;
            }
        }
    }

    /// <summary>
    /// If the node's ViewModel implements <see cref="ICompileTimeSink"/>,
    /// notify it of the current execution event.
    /// </summary>
    private static void NotifyExecutionSink(CompiledItem item,
        object? parameter, ExecutionEvent @event, int totalCount)
    {
        if (item.Node is ICompileTimeSink sink)
        {
            var ctx = new ExecutionContext(
                item.Order, totalCount, parameter, @event, item);
            sink.OnExecutionEvent(ctx);
        }
    }

    /// <summary>
    /// Notify all items that implement <see cref="ICompileTimeSink"/>.
    /// Used for chain-level events where no single item is responsible.
    /// </summary>
    private void NotifyAllExecutionSinks(object? parameter, ExecutionEvent @event, int totalCount)
    {
        foreach (var item in _items)
            NotifyExecutionSink(item, parameter, @event, totalCount);
    }
}
