using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Compiler runtime session-context interface. One runtime execution session (UID / logs / shared
/// variables / execution position). Inherits <see cref="ITaskContext"/>: when compiled execution drives a
/// node, the engine passes this session object **as the task context** directly into
/// <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/>.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "编译器运行时会话上下文（继承 ITaskContext）：UID/日志/共享变量/执行位置，编译驱动时作为任务上下文传入节点")]
[AgentContext(AgentLanguages.English, "Compiler runtime session context (inherits ITaskContext): UID/logs/shared variables/execution position, passed to nodes as the task context during compiled runs")]
public interface IRuntimeContext : ITaskContext
{
    /// <summary>Unique identifier of this run session.</summary>
    Guid Uid { get; set; }

    /// <summary>Next execution sequence number (auto-incremented).</summary>
    int Sequence { get; set; }

    /// <summary>Runtime logs (with sequence prefixes).</summary>
    ObservableCollection<string> Logs { get; set; }

    /// <summary>The CompileSegment currently executing.</summary>
    CompileSegment? CurrentEntry { get; set; }

    /// <summary>Index of the current node within its chain.</summary>
    int NodeIndex { get; set; }

    /// <summary>Current branch key.</summary>
    object? BranchKey { get; set; }

    /// <summary>Current attempt count.</summary>
    int Attempt { get; set; }

    /// <summary>Whether a run is in progress.</summary>
    bool IsRunning { get; set; }

    /// <summary>Run status string (Idle/Running/Completed/Stopped).</summary>
    string Status { get; set; }

    /// <summary>Current execution status code = compile-time fixed number.</summary>
    int CurrentOrder { get; set; }

    /// <summary>
    /// Optional target node a run should track (result/terminal runs set this). The engine sets
    /// <see cref="TargetReached"/> once the node matching <see cref="Target"/> is actually driven.
    /// </summary>
    IWorkflowNodeViewModel? Target { get; set; }

    /// <summary>Whether the <see cref="Target"/> node was actually driven during this run (false = its branch was not taken / condition not satisfied).</summary>
    bool TargetReached { get; set; }

    /// <summary>
    /// Chained result (shadows <see cref="IContext.Data"/> to add a setter): after driving each node the engine
    /// writes back the <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/> return value for downstream nodes
    /// to read. The base data-flow carrier <see cref="ITaskContext"/> stays read-only; only the runtime session
    /// is writable.
    /// </summary>
    new object? Data { get; set; }

    /// <summary>
    /// Whether this drive reported anything at all — a warning, an error, or an exception the node did not catch.
    /// The engine clears it before each drive; <b>what</b> was reported is what decides where the run goes.
    /// </summary>
    bool RedirectRequested { get; set; }

    /// <summary>
    /// Whether the flow ended early, leaving the run <c>"Stopped"</c> with status code <c>-1</c>: an
    /// <see cref="Error"/> (or an uncaught exception) that no <see cref="IRedirectable"/> could place, a router or
    /// redirect contract that threw, or the redirect cap.
    /// </summary>
    bool EndedWithError { get; set; }

    /// <summary>The engine-requested redirect target Order (may be cross-chain). <see cref="RuntimeEngine.RunAsync"/> re-runs the whole graph with it.</summary>
    int? PendingRedirectTarget { get; set; }

    /// <summary>
    /// The "current re-run target Order" the engine writes at the start of each pass (null on the first pass).
    /// The output collector uses it to tell the two kinds of skipped nodes apart: nodes before the target are the
    /// contract-preserved prefix (Order &lt; that value); nodes after the target not re-driven are stale branches.
    /// </summary>
    [AgentContext(AgentLanguages.Chinese, "当前重定向目标 Order：引擎每 pass 开头写入（首 pass 为 null），产物收集用它区分契约保留 prefix 与陈旧分支")]
    [AgentContext(AgentLanguages.English, "Active redirect target Order set by the engine at the start of each pass (null on the first pass); the output collector uses it to distinguish contract-preserved prefix from stale branches")]
    int? ActiveRedirectTarget { get; set; }

    /// <summary>Pushes a plain log line (with a sequence prefix).</summary>
    void Log(string entry);

    /// <summary>
    /// Pushes an exception/error log line (sequence prefix with an [Error] marker) and <b>stops the run</b>: the
    /// engine treats this drive as having produced <c>null</c> and ends the whole flow with status -1 — unless the
    /// node implements <see cref="IRedirectable"/>, whose answer then decides where the run goes instead.
    /// </summary>
    /// <remarks>
    /// Synchronous and void on purpose: this runs inside a node's frame and must never block or throw. Use
    /// <see cref="ErrorAsync"/> to also hand the host's <see cref="IExecutionErrorSink"/> a record.
    /// </remarks>
    void Error(string message);

    /// <summary>
    /// Pushes a warning log line (sequence prefix with a [Warning] marker). <b>A note, not a stop:</b> the run
    /// carries on with whatever the node returned — that value is not discarded, and no redirect is asked for.
    /// </summary>
    /// <remarks>Use <see cref="Error"/> for a failure that should end the run, and <see cref="Log"/> for something
    /// that is not worth a marker. <see cref="WarnAsync"/> adds the host's record.</remarks>
    void Warn(string message);

    /// <summary>
    /// <see cref="Error"/> plus the host's record: writes the line and awaits
    /// <see cref="IExecutionErrorSink.OnErrorAsync"/> with <see cref="ExecutionReportLevel.Error"/>.
    /// </summary>
    /// <remarks>
    /// Awaiting is the point — it keeps the host's records in the order the run made them, and tells the node that
    /// its report landed. A sink that throws is swallowed and logged; a report never fails the run that made it.
    /// </remarks>
    Task ErrorAsync(string message);

    /// <summary>
    /// <see cref="Warn"/> plus the host's record: writes the line and awaits
    /// <see cref="IExecutionErrorSink.OnErrorAsync"/> with <see cref="ExecutionReportLevel.Warning"/>.
    /// </summary>
    Task WarnAsync(string message);

    /// <summary>Writes a shared variable (ignored when the key is empty).</summary>
    void Set(string key, object? value);

    /// <summary>Reads a shared variable.</summary>
    bool TryGet(string key, out object? value);

    /// <summary>Registers a node's output for this run (written by the engine after DriveAsync drives it), stamped with the current pass, for downstream join points to aggregate.</summary>
    [AgentContext(AgentLanguages.Chinese, "登记节点本次运行的产物：引擎逐节点驱动后写入，带当前 pass 戳；多输入汇合点按输入组聚合")]
    [AgentContext(AgentLanguages.English, "Register a node's output for this run stamped with the current attempt: the engine writes it after driving each node, and multi-input joins aggregate per input group")]
    void RegisterOutput(IWorkflowNodeViewModel node, object? value);

    /// <summary>Clears the output registry (once at the start of each <see cref="RuntimeEngine.RunAsync"/>).</summary>
    [AgentContext(AgentLanguages.Chinese, "清空产物登记表：每次 RunAsync 开始清空一次，重定向重跑不清空（由 pass 戳过滤陈旧产物）")]
    [AgentContext(AgentLanguages.English, "Clear the product registry once at the start of each RunAsync (not cleared on redirect re-runs; stale outputs are filtered by pass stamp)")]
    void ResetOutputs();

    /// <summary>
    /// Collects the outputs of a group of input nodes into a read-only dictionary (Key = source Node reference
    /// identity, Value = that node's output).
    /// Filter rule: only keep outputs "actually run this pass" (pass stamp == current Attempt) or the contract-preserved
    /// prefix before the redirect target (source Order &lt; ActiveRedirectTarget; the resume contract assumes its result
    /// did not change); unregistered nodes are absent — <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue"/>
    /// returns false. Capacity is pre-sized by the input-source count.
    /// </summary>
    [AgentContext(AgentLanguages.Chinese, "按输入组收集产物为只读字典：仅本 pass 产物 + 目标前契约保留 prefix；未登记的来源不包含（TryGetValue 返回 false）")]
    [AgentContext(AgentLanguages.English, "Collect outputs for a group of input nodes as a read-only dictionary: only this pass's outputs plus the contract-preserved prefix before the redirect target; unregistered sources are absent (TryGetValue returns false)")]
    IReadOnlyDictionary<IWorkflowNodeViewModel, object?> CollectGroupedInputs(IEnumerable<IWorkflowNodeViewModel> inputNodes);
}
