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

    // ── Shared context ──
    [VeloxProperty] private Guid _uid = Guid.NewGuid();
    [VeloxProperty] private int _sequence = 0;
    [VeloxProperty] private ObservableCollection<string> _logs = [];

    // ── Execution position / decision state (maintained by the engine) ──
    [VeloxProperty] private ActionEntry? _currentEntry;
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

    /// <summary>Nodes/the engine push a plain log line (with a sequence prefix).</summary>
    public void Log(string entry) => _logs.Add($"{Next():00}. {entry}");

    /// <summary>Nodes/the engine push an exception/error message (sequence prefix with a ✗ marker). Also requests a redirect.</summary>
    public void Error(string message)
    {
        _logs.Add($"{Next():00}. ✗ {message}");
        RedirectRequested = true;
    }

    /// <summary>Nodes/the engine push a warning message (sequence prefix with a ⚠ marker). Also requests a redirect.</summary>
    public void Warn(string message)
    {
        _logs.Add($"{Next():00}. ⚠ {message}");
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
