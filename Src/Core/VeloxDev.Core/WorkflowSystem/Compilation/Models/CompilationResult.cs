namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// The compiled output: a read-only ordered list of items.
/// Call <see cref="ExecuteAsync"/> to run them in sequence.
/// Lock/UnLock is used to prevent nodes from self-propagating
/// during compiled execution — each node is locked before the run
/// so that any broadcast attempts inside WorkAsync are queued,
/// then unlocked after all nodes complete.
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
    /// Execute all items in order using the Lock/UnLock mechanism.
    ///
    /// For each item:
    ///   1. Lock — broadcast attempts inside WorkAsync are queued.
    ///   2. Subscribe to WorkCommand.Failed for error redirect / retry.
    ///   3. Execute WorkCommand.
    ///   4. Unlock.
    ///
    /// When Allow mode is active and a loop is detected, consecutive loop iterations
    /// are tracked. If the loop tail jumps back to the loop entry consecutively,
    /// a counter increments; if MaxLoopCount is exceeded, the circuit breaker fires.
    ///
    /// After all items complete, all nodes are unlocked so queued commands can drain.
    /// <returns>The last node's result, or the initial parameter if no node produced a result.</returns>
    /// </summary>
    public async Task<object?> ExecuteAsync(object? parameter = null, CancellationToken ct = default)
    {
        // 第一阶段：锁定所有节点，防止 WorkAsync 内自行广播
        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();
            item.Node.WorkCommand.Lock();
        }

        object? currentParam = parameter;

        try
        {
            int consecutiveLoopCount = 0;
            CompiledItem? lastExecuted = null;

            foreach (var item in _items)
            {
                ct.ThrowIfCancellationRequested();

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

                // 执行，传入链式参数
                var ctx = new WorkContext(currentParam);

                try
                {
                    await item.Node.WorkCommand.ExecuteAsync(ctx);

                    // 结果链传递：将节点输出传给下一个
                    if (item.Node.WorkResult is not null)
                        currentParam = item.Node.WorkResult;
                }
                catch
                {
                    // 错误已由 Failed 事件处理
                }
                finally
                {
                    // 通知：执行后（无论成功还是失败）
                    NotifyExecutionSink(item, currentParam, ExecutionEvent.AfterExecute);
                    item.UnsubscribeError();
                    lastExecuted = item;
                }
            }

            // 通知：全链执行完毕
            NotifyAllExecutionSinks(currentParam, ExecutionEvent.OnCompleted);

            return currentParam;
        }
        finally
        {
            // 第二阶段：解锁所有节点，排队广播开始释放
            foreach (var item in _items)
            {
                item.Node.WorkCommand.UnLock();
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
