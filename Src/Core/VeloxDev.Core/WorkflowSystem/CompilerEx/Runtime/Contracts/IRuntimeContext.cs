using System.Collections.ObjectModel;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Compiler runtime session-context interface. One runtime execution session (UID / logs / shared
/// variables / execution position). Inherits <see cref="ITaskContext"/>: when compiled execution drives a
/// node, the engine passes this session object **as the task context** directly into
/// <see cref="INodeHelper.ReceiveAsync"/>.
/// </summary>
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
    INodeComponent? Target { get; set; }

    /// <summary>Whether the <see cref="Target"/> node was actually driven during this run (false = its branch was not taken / condition not satisfied).</summary>
    bool TargetReached { get; set; }

    /// <summary>
    /// Chained result (shadows <see cref="IContext.Data"/> to add a setter): after driving each node the engine
    /// writes back the <see cref="INodeHelper.ReceiveAsync"/> return value for downstream nodes
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

    /// <summary>
    /// The engine-requested redirect target Order (may be cross-chain). The pass that sets it ends at the
    /// requesting node; <see cref="RuntimeEngine.RunAsync"/> then re-runs the whole graph with it, skipping
    /// Order &lt; target.
    /// </summary>
    int? PendingRedirectTarget { get; set; }

    /// <summary>
    /// The "current re-run target Order" the engine writes at the start of each pass (null on the first pass).
    /// The output collector uses it to tell the two kinds of skipped nodes apart: nodes before the target are the
    /// contract-preserved prefix (Order &lt; that value); nodes after the target not re-driven are stale branches.
    /// </summary>
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

    /// <summary>Registers a node's output for this run (written by the engine after a drive — a failed drive registers <see langword="null"/>), stamped with the current pass, for downstream join points to aggregate.</summary>
    void RegisterOutput(INodeComponent node, object? value);

    /// <summary>Clears the output registry (once at the start of each <see cref="RuntimeEngine.RunAsync"/>).</summary>
    void ResetOutputs();

    /// <summary>
    /// Collects the outputs of a group of input nodes into a read-only dictionary (Key = source Node reference
    /// identity, Value = that node's output).
    /// Filter rule: only keep outputs "actually run this pass" (pass stamp == current Attempt) or the contract-preserved
    /// prefix before the redirect target (source Order &lt; ActiveRedirectTarget; the resume contract assumes its result
    /// did not change); unregistered nodes are absent — <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue"/>
    /// returns false. Capacity is pre-sized by the input-source count.
    /// </summary>
    IReadOnlyDictionary<INodeComponent, object?> CollectGroupedInputs(IEnumerable<INodeComponent> inputNodes);
}
