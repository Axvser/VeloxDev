using System.IO;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// The compiled output: a read-only ordered list of items.
/// Call <see cref="ExecuteAsync(object?, System.Threading.CancellationToken)"/> to run them in sequence.
///
/// Execution model (compiled mode):
/// The compiler's <see cref="CompilationResult"/> owns the execution order.
/// Helper classes (<c>WorkAsync</c>) should NOT self-propagate via broadcast
/// when running inside a compiled chain — instead they mutate the parameter
/// in-place and the compiler passes <c>currentParam</c> to the next item.
/// </summary>
public sealed class CompilationResult
{
    private readonly IReadOnlyList<CompiledItem> _items;
    private readonly IDiagnosticLogger? _logger;
    private readonly Guid _machineId;

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
        CycleHandling cycleHandling = CycleHandling.Throw,
        IDiagnosticLogger? logger = null, Guid machineId = default)
    {
        _items = items;
        _logger = logger;
        _machineId = machineId == default ? Guid.NewGuid() : machineId;
        Mode = mode;
        Direction = direction;
        Scope = scope;
        HasCycle = hasCycle;
        CycleHandling = cycleHandling;
    }

    // ── DiagnosticContext helpers ─────────────────────────────────────────

    private DiagnosticContext Ctx(string contentType, string message) =>
        new(DateTimeOffset.UtcNow, _machineId, contentType, "Info", message);

    private DiagnosticContext CtxWarn(string contentType, string message) =>
        new(DateTimeOffset.UtcNow, _machineId, contentType, "Warning", message);

    private DiagnosticContext CtxErr(string contentType, string message) =>
        new(DateTimeOffset.UtcNow, _machineId, contentType, "Error", message);

    /// <summary>
    /// Execute all items in deterministic order.
    ///
    /// For each item:
    ///   1. Subscribe to WorkCommand.Failed for error capture.
    ///   2. Notify <see cref="ICompileTimeSink"/> (BeforeExecute).
    ///   3. Execute WorkCommand and wait for actual completion via Exited event.
    ///   4. Forward <c>currentParam</c> to the next item (helpers mutate the parameter in-place).
    ///   5. On failure: notify OnError, then if <see cref="CompiledItem.ErrorRedirectId"/> is set,
    ///      execute the target node asynchronously and pass through its result.
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
    /// <param name="parameter">Initial parameter passed through the chain.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The last node's result, or the initial parameter if no node produced a result.</returns>
    /// </summary>
    public Task<object?> ExecuteAsync(object? parameter = null, CancellationToken ct = default)
        => ExecuteAsync(parameter, debugOutput: null, ct);

    /// <summary>
    /// Execute all items with an optional <see cref="TextWriter"/> for debug diagnostics.
    /// Each execution-phase event is written as a structured <see cref="DiagnosticContext"/>
    /// entry to the provided writer in real-time.
    /// </summary>
    /// <param name="parameter">Initial parameter passed through the chain.</param>
    /// <param name="debugOutput">
    /// Optional <see cref="TextWriter"/> for streaming execution diagnostics.
    /// The caller retains ownership and must dispose it independently.
    /// When <c>null</c>, no execution-phase diagnostics are emitted.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The last node's result, or the initial parameter if no node produced a result.</returns>
    public async Task<object?> ExecuteAsync(object? parameter, TextWriter? debugOutput, CancellationToken ct = default)
    {
        IDiagnosticLogger? execLogger = _logger;

        if (debugOutput is not null)
        {
            execLogger = new SynchronousFileLogger(debugOutput, ownsWriter: false);
        }

        return await ExecuteCoreAsync(parameter, execLogger, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute all items and write execution diagnostics to the specified file path.
    /// The file is opened in append mode; the directory is created if it does not exist.
    /// </summary>
    /// <param name="parameter">Initial parameter passed through the chain.</param>
    /// <param name="debugFilePath">
    /// Absolute or relative path for the debug log file. Resolved against
    /// <see cref="AppContext.BaseDirectory"/> when relative.
    /// The file is created/opened in append mode with shared read access.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The last node's result, or the initial parameter if no node produced a result.</returns>
    public async Task<object?> ExecuteAsync(object? parameter, string debugFilePath, CancellationToken ct = default)
    {
        var fullPath = Path.IsPathRooted(debugFilePath)
            ? debugFilePath
            : Path.Combine(AppContext.BaseDirectory, debugFilePath);

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var fileStream = new FileStream(fullPath,
            FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var writer = new StreamWriter(fileStream) { AutoFlush = true };

        using (var scopedLogger = new SynchronousFileLogger(writer, ownsWriter: true))
        {
            return await ExecuteCoreAsync(parameter, scopedLogger, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Core execution loop shared by all <see cref="ExecuteAsync(object?, System.Threading.CancellationToken)"/> overloads.
    /// </summary>
    private async Task<object?> ExecuteCoreAsync(object? parameter, IDiagnosticLogger? logger, CancellationToken ct)
    {
        object? currentParam = parameter;
        var skippedItems = new HashSet<int>(); // 未选中分支的独占项 ID
        var totalCount = _items.Count;

        logger?.Log(Ctx("Execute", $"start {totalCount}i"));

        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();

            // 跳过被路由分支排除的项
            if (skippedItems.Contains(item.Id))
            {
                logger?.Log(Ctx("Execute", $"[{item.Id}] skip"));
                NotifyExecutionSink(item, currentParam, ExecutionEvent.BeforeExecute, totalCount);
                NotifyExecutionSink(item, currentParam, ExecutionEvent.AfterExecute, totalCount);
                continue;
            }

            logger?.Log(Ctx("Execute", $"[{item.Id}] exec o{item.Order} d{item.Depth}"));

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
                logger?.LogWarning(CtxWarn("Execute", $"[{item.Id}] cancel"));
                item.UnsubscribeError();
                throw;
            }
            catch (Exception ex)
            {
                // 执行中抛出的其他异常转储到 FailureException，统一走后面的错误处理路径
                logger?.LogError(CtxErr("Execute", $"[{item.Id}] {ex.Message}"));
                item.FailureException = ex;
            }

            // 错误处理：如果节点失败，检查是否需要重定向
            if (item.FailureException is not null)
            {
                logger?.Log(Ctx("Execute", $"[{item.Id}] fail r={item.ErrorRedirectId}"));
                NotifyExecutionSink(item, currentParam, ExecutionEvent.OnError, totalCount);

                if (item.ErrorRedirectId.HasValue)
                {
                    var target = _items.FirstOrDefault(i => i.Id == item.ErrorRedirectId.Value);
                    if (target is not null)
                    {
                        logger?.Log(Ctx("Execute", $"[{item.Id}] -> [{target.Id}]"));
                        var errorCtx = new ErrorContext(
                            item.Id, item.FailureException, currentParam, currentParam);
                        // 异步执行重定向目标
                        await ExecuteItemAsync(target, new WorkContext(errorCtx), ct);
                        // 标记重定向目标，防止在主循环中被重复执行
                        if (!skippedItems.Contains(target.Id))
                            skippedItems.Add(target.Id);
                    }
                }
            }
            else
            {
                // 节点执行成功
                logger?.Log(Ctx("Execute", $"[{item.Id}] done"));
            }

            // 路由分支排除：如果当前节点是路由器，根据选中的 key 跳过未选分支的独占项
            if (item.FailureException is null &&
                item.BranchExclusiveItems is not null &&
                item.Node is ICompileTimeRouter router)
            {
                var chosenKey = router.GetCurrentRouteKey();
                logger?.Log(Ctx("Execute", $"[{item.Id}] key={chosenKey} ex={item.BranchExclusiveItems.Count}"));
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

        logger?.Log(Ctx("Execute", $"done {totalCount}i"));
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
