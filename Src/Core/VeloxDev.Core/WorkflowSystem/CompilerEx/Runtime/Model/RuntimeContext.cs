using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// One runtime execution session (also the public VM carrying the UID):
/// shared context (UID / sequence / logs / shared variables) plus execution position and decision
/// state (maintained by the engine, bound to UI progress).
/// Nodes implement <see cref="IRuntimeAware"/> and the engine injects this object before driving.
/// Inherits <see cref="ITaskContext"/>: the compiler passes this session object directly into
/// <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/> and writes <see cref="Data"/> per node.
/// </summary>
public sealed partial class RuntimeContext : IRuntimeContext
{
    /// <summary>Runtime execution session — always false (only the compile phase is true).</summary>
    public bool IsCompilePhase => false;

    /// <summary>Optional target node the caller wants this run to reach (result/terminal runs).</summary>
    public IWorkflowNodeViewModel? Target { get; set; }

    /// <summary>Whether <see cref="Target"/> was actually driven this run (false = condition not satisfied / branch not taken).</summary>
    public bool TargetReached { get; set; }

    // ── Shared context ──
    [VeloxProperty] private Guid _uid = Guid.NewGuid();
    [VeloxProperty] private int _sequence = 0;
    [VeloxProperty] private ObservableCollection<string> _logs = [];

    // ── Execution position / decision state (maintained by the engine) ──
    [VeloxProperty] private CompileSegment? _currentEntry;
    [VeloxProperty] private int _nodeIndex = -1;
    [VeloxProperty] private object? _branchKey;
    [VeloxProperty] private int _attempt;
    [VeloxProperty] private bool _isRunning;
    [VeloxProperty] private string _status = "Idle";

    /// <summary>
    /// Current execution status code = the **compile-time fixed number** (CompileContext.Order) of the
    /// node currently executing. At runtime it only jumps between these fixed numbers, never renumbers.
    /// </summary>
    [VeloxProperty] private int _currentOrder = -1;

    // ── Data-flow payload (injected per node by the compiler while driving; exposed as ITaskContext) ──
    [VeloxProperty] private object? _data;
    [VeloxProperty] private IWorkflowSlotViewModel? _sender;
    [VeloxProperty] private IWorkflowSlotViewModel? _receiver;

    // Shared variables (blackboard): readable/writable by nodes, the engine, and the UI; not directly
    // UI-bound, accessed through methods
    private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);

    // Output registry (Key = source Node reference identity, Value = the output registered this pass + pass stamp):
    // the engine registers after driving each node, and join points aggregate per input group; the pass stamp
    // distinguishes "actually ran this pass" from "old outputs skipped on a re-run".
    private readonly Dictionary<IWorkflowNodeViewModel, (int Attempt, object? Value)> _outputs =
        new(WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);

    // 这次驱动报了什么级别：null = 干净返回，Warning = Warn()，Error = Error() 或抛异常。
    // 引擎按它决定停不停 —— Warning 完全不影响控制流（值照常传下去），Error 在没有处理者时结束整轮。
    internal ExecutionReportLevel? ReportedLevel { get; set; }

    /// <summary>
    /// Whether this drive reported anything at all — a warning, an error, or a throw. The engine clears it before
    /// each drive; what it reported is what decides where the run goes.
    /// </summary>
    /// <remarks>The setter is here because the contract declares one; assigning <c>true</c> counts as the gravest reading, an error.</remarks>
    public bool RedirectRequested
    {
        get => ReportedLevel is not null;
        set => ReportedLevel = value ? ExecutionReportLevel.Error : null;
    }

    /// <summary>Whether the flow ended early because "the node errored but does not implement <see cref="IRedirectable"/>" (status set to -1).</summary>
    public bool EndedWithError { get; set; }

    /// <summary>
    /// How many branches of one fan-out may be in flight at once; <c>null</c> (the default) means no cap.
    /// </summary>
    /// <remarks>
    /// A fan-out starts its branches concurrently, so a wide one — thirty processors, say — starts thirty
    /// pieces of work at once. Set this when the branches are heavy (each one spawning a process, holding a
    /// large buffer) and the machine would rather work through them in waves.
    /// <para>
    /// Not on <see cref="IRuntimeContext"/> on purpose: adding a member to that contract would break every
    /// external implementation, and this is engine policy rather than session state. A custom context simply
    /// gets the uncapped behaviour.
    /// </para>
    /// </remarks>
    public int? MaxParallelBranches { get; set; }

    /// <summary>The engine-requested redirect target Order (may be cross-chain). RunAsync re-runs the whole graph with it.</summary>
    public int? PendingRedirectTarget { get; set; }

    /// <summary>
    /// The "current re-run target Order" the engine writes at the start of each pass (null on the first pass).
    /// The output collector uses it to tell the two kinds of skipped nodes apart: nodes before the target are the
    /// contract-preserved prefix (Order &lt; that value); nodes after the target not re-driven are stale branches.
    /// </summary>
    public int? ActiveRedirectTarget { get; set; }

    /// <summary>Gets the next execution sequence number (auto-incremented).</summary>
    public int Next() => Interlocked.Increment(ref _sequence);

    /// <summary>
    /// Where the run's lines are diverted to, in addition to <see cref="Logs"/>; <c>null</c> (the default) keeps
    /// them in memory only. Set it to a file-backed writer to stop trading memory for history — see
    /// <see cref="ILogWriter"/> for the threading contract.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="IRuntimeContext"/>: adding a member there would break every external
    /// implementation, and this is host policy rather than session state (a custom context simply keeps the
    /// in-memory behaviour).
    /// </remarks>
    public ILogWriter? LogWriter { get; set; }

    /// <summary>
    /// How many lines <see cref="Logs"/> keeps — the oldest are dropped first. <c>null</c> (the default) keeps
    /// everything; <c>0</c> keeps none while <see cref="LogWriter"/> still receives every line.
    /// </summary>
    /// <remarks>
    /// Pair it with <see cref="LogWriter"/>: the writer is the full-fidelity record, this is the bounded view a
    /// host can leave in memory. Unbounded by default on purpose — the Agent's <c>RunCompiledWorkflow</c> tool
    /// serializes <see cref="Logs"/> into its result, so trimming by default would silently change what the model
    /// is shown.
    /// </remarks>
    public int? MaxRetainedLogs { get; set; }

    // ── 可选能力 ─────────────────────────────────────────────────────────────
    // 默认全关，且刻意不放进 IRuntimeContext：给那个契约加成员会破坏每个外部实现（与上面几条策略同理）。
    // 引擎靠转型到具体类取它们 —— 宿主自带 IRuntimeContext 实现时一个也拿不到，与 MaxParallelBranches 同样的取舍。

    /// <summary>Where the run pauses between nodes; <c>null</c> (the default) means it never pauses.</summary>
    /// <seealso cref="IExecutionGate"/>
    public IExecutionGate? ExecutionGate { get; set; }

    /// <summary>Watches what the run does; <c>null</c> (the default) observes nothing.</summary>
    /// <seealso cref="IExecutionObserver"/>
    public IExecutionObserver? Observer { get; set; }

    /// <summary>Decides whether a node that threw gets another go; <c>null</c> (the default) means it does not.</summary>
    /// <seealso cref="INodeRetryPolicy"/>
    public INodeRetryPolicy? RetryPolicy { get; set; }

    /// <summary>Receives every failure as a record; <c>null</c> (the default) leaves them as log lines only.</summary>
    /// <seealso cref="IExecutionErrorSink"/>
    public IExecutionErrorSink? ErrorSink { get; set; }

    /// <summary>
    /// Told about the nodes a run that ends badly already drove, most recent first; <c>null</c> (the default) tells
    /// nobody.
    /// </summary>
    /// <seealso cref="IExecutionCompensation"/>
    public IExecutionCompensation? Compensation { get; set; }

    /// <summary>
    /// Where the run's place is written down after each node succeeds; <c>null</c> (the default) keeps no place.
    /// Pair it with <see cref="RuntimeEngine.RunAsync"/>'s <c>resumeFrom</c> to carry a run across a stop.
    /// </summary>
    /// <seealso cref="IExecutionCheckpointStore"/>
    public IExecutionCheckpointStore? CheckpointStore { get; set; }

    /// <summary>
    /// How the last run ended, as a precise reading of <see cref="Status"/> + <see cref="EndedWithError"/>;
    /// <see cref="RunOutcome.Unknown"/> until a run ends.
    /// </summary>
    public RunOutcome Outcome { get; set; } = RunOutcome.Unknown;

    // 本轮成功驱动的节点及其产物，按驱动序，补偿按它逆序走。
    // 有次序（_outputs 没有），且每个节点只占一条：重定向会重跑，重跑过的节点移到末尾，而不是记两条、补偿两次。
    // 只有 ResetOutputs 清它，绝不按趟清 —— 于是重定向跳过的前缀（第一趟驱动过、之后再不驱动）也仍在这一轮该负责的范围内。
    private readonly List<(IWorkflowNodeViewModel Node, object? Output)> _completed = [];

    // 本轮的成功列表（含产物），补偿器按它逆序回调。
    internal IReadOnlyList<(IWorkflowNodeViewModel Node, object? Output)> CompletedThisRun => [.. _completed];

    // 检查点用：这次运行那张图的「节点 → 键」有序表，RunAsync 开头算一次。形状与键都从它来。
    internal IReadOnlyList<(IWorkflowNodeViewModel Node, string Key)>? CheckpointNodes { get; set; }

    // 把当前状态打成一份检查点：本 pass 的产物 + 重定向保留的前缀，外加运行自身的状态（Attempt / 跳过的目标 / 载荷）。
    internal ExecutionCheckpoint Snapshot()
    {
        var nodes = CheckpointNodes;
        var outputs = new Dictionary<string, object?>();
        if (nodes is not null)
        {
            // 显式取 Key/Value：KeyValuePair 的 Deconstruct 在 netstandard2.0 / net461 上不存在。
            foreach (var pair in _outputs)
            {
                if (!IsCurrentPassOrPreserved(pair.Key, pair.Value)) continue;
                if (!TryKeyOf(nodes, pair.Key, out var key)) continue;
                outputs[key] = Normalize(pair.Value.Value, nodes);
            }
        }

        return new ExecutionCheckpoint
        {
            Attempt = Attempt,
            ActiveRedirectTarget = ActiveRedirectTarget,
            Data = Normalize(Data, nodes),
            Outputs = outputs,
            Shape = nodes is null ? [] : [.. nodes.Select(entry => entry.Key)],
            Types = nodes is null ? [] : [.. nodes.Select(entry => entry.Node.GetType().Name)],
        };
    }

    private static bool TryKeyOf(
        IReadOnlyList<(IWorkflowNodeViewModel Node, string Key)> nodes, IWorkflowNodeViewModel node, out string key)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!ReferenceEquals(nodes[i].Node, node)) continue;
            key = nodes[i].Key;
            return true;
        }
        key = string.Empty;
        return false;
    }

    // 汇合载荷是按「节点引用」键的字典 ⇒ 写进检查点前必须换成节点键：引用写不进文件，而序列化会顺着它把
    // 整棵树拖进去（与 CompiledGraphEx 排除 Parent 同一个坑）。运行中的对象不动，只换快照里这一份。
    private static object? Normalize(object? value, IReadOnlyList<(IWorkflowNodeViewModel Node, string Key)>? nodes)
    {
        if (value is not IGroupData group || nodes is null) return value;
        var map = new Dictionary<string, object?>();
        foreach (var entry in group)
        {
            if (TryKeyOf(nodes, entry.Key, out var key))
                map[key] = Normalize(entry.Value, nodes);
        }
        return map;
    }

    /// <summary>Raised when <see cref="LogWriter"/> throws. The line is still kept in <see cref="Logs"/> and the run
    /// carries on — diagnostics never change what the run does.</summary>
    public event EventHandler<LogWriteFailedEventArgs>? LogWriteFailed;

    /// <summary>Appends one line: the writer first (it is the complete record), then the retained view.</summary>
    private void AppendLog(string line)
    {
        // The line is handed to the writer before the retention check, and a writer that throws is not allowed to
        // reach the run: AppendLog runs inside node frames, so an escaping exception would surface as a *node*
        // failure and the engine would read it as a redirect request. AgentPipeline isolates its stages for the
        // same reason; here the equivalent is to report and drop.
        if (LogWriter is { } writer)
        {
            try { writer.Write(line); }
            catch (Exception ex) { LogWriteFailed?.Invoke(this, new LogWriteFailedEventArgs(line, ex)); }
        }

        _logs.Add(line);

        if (MaxRetainedLogs is int cap && cap >= 0)
            while (_logs.Count > cap) _logs.RemoveAt(0);
    }

    /// <summary>Nodes/the engine push a plain log line (with a sequence prefix).</summary>
    public void Log(string entry) => AppendLog($"{Next():00}. {entry}");

    /// <inheritdoc />
    public void Error(string message)
    {
        AppendLog($"{Next():00}. [Error] {message}");
        ReportedLevel = ExecutionReportLevel.Error;
    }

    /// <inheritdoc />
    public void Warn(string message)
    {
        AppendLog($"{Next():00}. [Warning] {message}");
        ReportedLevel = ExecutionReportLevel.Warning;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The line and the record, in that order: the log is the complete record of the run and must keep it even
    /// when no sink is configured or the sink fails.
    /// </remarks>
    public async Task ErrorAsync(string message)
    {
        Error(message);
        await ReportNodeAsync(CurrentNode, ExecutionReportLevel.Error, message);
    }

    /// <inheritdoc />
    public async Task WarnAsync(string message)
    {
        Warn(message);
        await ReportNodeAsync(CurrentNode, ExecutionReportLevel.Warning, message);
    }

    /// <summary>
    /// The node being driven right now, written by the engine before each drive. A node's own report needs it: a
    /// structured record without the node that made it is not worth handing to a host.
    /// </summary>
    /// <remarks>
    /// The engine writes it on whichever context it is driving with, so a fan-out's branches keep their own — see
    /// <see cref="BranchRuntimeContext.CurrentNode"/>, and the same reason <see cref="Data"/> is branch-local.
    /// </remarks>
    internal IWorkflowNodeViewModel? CurrentNode { get; set; }

    // 把节点自己报的这条交给宿主的 sink。node 由调用方给出，不查会话 —— 扇出里门面自带当前节点，
    // 问会话要会被交错的分支互相覆盖。报告不得失败：sink 抛异常只换回一行日志（同 NotifyErrorAsync）。
    // 令牌恒为 None：这是记录而不是可打断的工作，且这条调用发生在节点帧里、拿不到运行令牌。
    internal async Task ReportNodeAsync(IWorkflowNodeViewModel? node, ExecutionReportLevel level, string message)
    {
        if (ErrorSink is not { } sink) return;
        var order = (node as ICompileTimeAware)?.CompileContext?.Order ?? -1;
        var record = new ExecutionError(ExecutionFailurePhase.Node, node, message, null, Attempt, order, level);
        try
        {
            await sink.OnErrorAsync(record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendLog($"{Next():00}. [ErrorSink] the report was not recorded: {ex.Message}");
        }
    }

    /// <summary>Writes a shared variable (ignored when the key is empty).</summary>
    public void Set(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _variables[key] = value;
    }

    /// <summary>Reads a shared variable.</summary>
    public bool TryGet(string key, out object? value) => _variables.TryGetValue(key, out value);

    /// <summary>Registers the node's output for this run (written by the engine after DriveAsync drives it), stamped with the current pass.</summary>
    public void RegisterOutput(IWorkflowNodeViewModel node, object? value)
    {
        if (node is null) return;
        _outputs[node] = (Attempt, value);

        // 登记即「这个节点本轮成功了」，补偿走的就是这个信号。放在这里而不是引擎的调用点上，是因为这个方法
        // 本身就是成功信号 —— 引擎在驱动不带异常返回后紧接着写它。重跑把它移到末尾：一轮结束时还立着的效果，
        // 是这个节点最近一次成功留下的那个。
        _completed.RemoveAll(entry => ReferenceEquals(entry.Node, node));
        _completed.Add((node, value));
    }

    /// <summary>
    /// Clears the output registry (once at the start of each RunAsync; redirect re-runs do not clear it → stale
    /// outputs are filtered by pass stamp, and <see cref="CompletedThisRun"/> keeps the whole run's successes).
    /// </summary>
    public void ResetOutputs()
    {
        _outputs.Clear();
        _completed.Clear();
    }

    /// <summary>
    /// Collects the outputs of a group of input nodes into a read-only dictionary; unregistered nodes are absent
    /// (TryGetValue returns false).
    /// Filter rule: only keep outputs "actually run this pass" (pass stamp == current Attempt) or the contract-preserved
    /// prefix before the redirect target (source Order &lt; ActiveRedirectTarget; the resume contract assumes its result
    /// did not change). Capacity is pre-sized by the input-source count, zero reallocations.
    /// </summary>
    public IReadOnlyDictionary<IWorkflowNodeViewModel, object?> CollectGroupedInputs(
        IEnumerable<IWorkflowNodeViewModel> inputNodes)
    {
        var capacity = inputNodes is IReadOnlyCollection<IWorkflowNodeViewModel> rc ? rc.Count : 0;
        var result = new Dictionary<IWorkflowNodeViewModel, object?>(capacity,
            WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);
        if (inputNodes is not null)
        {
            foreach (var n in inputNodes)
                if (n is not null && _outputs.TryGetValue(n, out var entry) && IsCurrentPassOrPreserved(n, entry))
                    result[n] = entry.Value;
        }
        return new ReadOnlyDictionary<IWorkflowNodeViewModel, object?>(result);
    }

    /// <summary>
    /// Whether a source's output is still considered valid: it was just registered this pass (pass stamp == Attempt),
    /// or the source is before the redirect target and belongs to the contract-preserved prefix (skipped without
    /// re-driving, but its result did not change).
    /// </summary>
    private bool IsCurrentPassOrPreserved(IWorkflowNodeViewModel source, (int Attempt, object? Value) entry)
        => entry.Attempt == Attempt
           || (ActiveRedirectTarget is int t
               && (source as ICompileTimeAware)?.CompileContext?.Order is int o && o < t);
}
