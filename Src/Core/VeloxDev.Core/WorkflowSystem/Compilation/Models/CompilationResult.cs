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
    ///   1. Subscribe to WorkCommand.Failed for error redirect / retry.
    ///   2. Notify <see cref="ICompileTimeSink"/> (BeforeExecute).
    ///   3. Execute WorkCommand and wait for actual completion via Exited event.
    ///   4. Read <see cref="IWorkflowNodeViewModel.WorkResult"/> and forward it to the next item.
    ///   5. Notify <see cref="ICompileTimeSink"/> (AfterExecute).
    ///
    /// Because <see cref="IVeloxCommand.ExecuteAsync"/> dispatches the command
    /// asynchronously (fire-and-forget internally), this method hooks the
    /// <see cref="IVeloxCommand.Exited"/> event to wait for genuine completion.
    ///
    /// The parameter is passed directly (NOT wrapped in <see cref="WorkContext"/>)
    /// so that downstream helpers can access the original object — e.g.
    /// <c>NetworkFlowContext.From(parameter)</c> works without unwrapping.
    ///
    /// When Allow mode is active and a loop is detected, consecutive loop iterations
    /// are tracked. If the loop tail jumps back to the loop entry consecutively,
    /// a counter increments; if MaxLoopCount is exceeded, the circuit breaker fires.
    ///
    /// <returns>The last node's result, or the initial parameter if no node produced a result.</returns>
    /// </summary>
    public async Task<object?> ExecuteAsync(object? parameter = null, CancellationToken ct = default)
    {
        object? currentParam = parameter;
        int consecutiveLoopCount = 0;
        CompiledItem? lastExecuted = null;
        var skippedItems = new HashSet<int>(); // 未选中分支的独占项 ID

        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();

            // 跳过被路由分支排除的项
            if (skippedItems.Contains(item.Id))
            {
                // 仍然发送 AfterExecute 通知以维持生命周期一致性
                NotifyExecutionSink(item, currentParam, ExecutionEvent.AfterExecute);
                continue;
            }

            // 环路追踪：检测连续回跳
            if (CycleHandling == CycleHandling.Allow && HasCycle && lastExecuted is not null)
            {
                if (item.IsLoopEntry && lastExecuted.Id == item.LoopTailId)
                {
                    consecutiveLoopCount++;
                    if (item.MaxLoopCount > 0 && consecutiveLoopCount > item.MaxLoopCount)
                    {
                        if (item.CircuitBreaker is not null)
                        {
                            item.CircuitBreaker(item);
                            continue;
                        }
                        throw new InvalidOperationException(
                            $"Loop exceeded maximum consecutive count ({item.MaxLoopCount}) " +
                            $"at item #{item.Id} (loop entry). Circuit breaker not registered.");
                    }
                }
                else
                {
                    consecutiveLoopCount = 0;
                }
            }

            item.SubscribeError(onFailed: (failedItem, exception) =>
            {
                // 通知失败的节点
                NotifyExecutionSink(failedItem, currentParam, ExecutionEvent.OnError);

                if (failedItem.ErrorRedirectId.HasValue)
                {
                    var target = _items.FirstOrDefault(i => i.Id == failedItem.ErrorRedirectId.Value);
                    var errorCtx = new ErrorContext(
                        failedItem.Id, exception, currentParam, currentParam);
                    var redirectCtx = new WorkContext(errorCtx);
                    target?.Node.WorkCommand.Execute(redirectCtx);
                }
            });

            // 通知：执行前
            NotifyExecutionSink(item, currentParam, ExecutionEvent.BeforeExecute);

            try
            {
                // 执行并等待真正完成
                await ExecuteItemAsync(item, currentParam, ct);

                // 结果链传递：将节点输出传给下一个
                if (item.Node.WorkResult is not null)
                    currentParam = item.Node.WorkResult;

                // 路由分支排除：如果当前节点是路由器，根据选中的 key 跳过未选分支的独占项
                if (item.BranchExclusiveItems is not null &&
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
            }
            catch
            {
                // 错误已由 Failed 事件处理
            }
            finally
            {
                NotifyExecutionSink(item, currentParam, ExecutionEvent.AfterExecute);
                item.UnsubscribeError();
                lastExecuted = item;
            }
        }

        // 通知：全链执行完毕
        NotifyAllExecutionSinks(currentParam, ExecutionEvent.OnCompleted);

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
        object? parameter, ExecutionEvent @event)
    {
        if (item.Node is ICompileTimeSink sink)
        {
            var ctx = new ExecutionContext(
                item.Order, 0, parameter, @event, item);
            sink.OnExecutionEvent(ctx);
        }
    }

    /// <summary>
    /// Notify all items that implement <see cref="ICompileTimeSink"/>.
    /// Used for chain-level events where no single item is responsible.
    /// </summary>
    private void NotifyAllExecutionSinks(object? parameter, ExecutionEvent @event)
    {
        foreach (var item in _items)
            NotifyExecutionSink(item, parameter, @event);
    }
}
