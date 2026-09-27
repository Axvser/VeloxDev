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

    /// <summary>
    /// Whether the node called <see cref="Error"/> or <see cref="Warn"/> during this drive (a redirect request).
    /// The engine clears it before each drive and checks it after. Read/written via <see cref="IRuntimeContext"/>.
    /// </summary>
    public bool RedirectRequested { get; set; }

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

    /// <summary>Nodes/the engine push an exception/error message (sequence prefix with an [Error] marker). Also requests a redirect.</summary>
    public void Error(string message)
    {
        AppendLog($"{Next():00}. [Error] {message}");
        RedirectRequested = true;
    }

    /// <summary>Nodes/the engine push a warning message (sequence prefix with a [Warning] marker). Also requests a redirect.</summary>
    public void Warn(string message)
    {
        AppendLog($"{Next():00}. [Warning] {message}");
        RedirectRequested = true;
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
    }

    /// <summary>Clears the output registry (once at the start of each RunAsync; redirect re-runs do not clear it → stale outputs are filtered by pass stamp).</summary>
    public void ResetOutputs() => _outputs.Clear();

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
