using System.Collections.ObjectModel;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// One parallel branch's view of a run session.
/// <para>
/// A fan-out's branches run concurrently, so the state a branch writes between an await and the node that reads
/// it back must not be shared with its siblings. It cannot be made flow-local either: a node calls
/// <see cref="Error"/> deep inside its own frame and the engine reads that flag several frames further up, and
/// an <see cref="System.Threading.AsyncLocal{T}"/> write does not travel back out to the caller — the first
/// attempt at this change failed exactly there, and the engine's existing redirect/prefix tests caught it.
/// </para>
/// <para>
/// So each branch gets its own object instead. It owns the payload and the redirect request; everything else —
/// identity, progress, the log, the output registry, the shared variables — is forwarded to the session the host
/// created. Forwarding the log rather than buffering it is deliberate: the run's lines must read in the order they
/// happened, interleaved branches included, so that a file-backed <see cref="ILogWriter"/> and
/// <see cref="IRuntimeContext.Logs"/> tell exactly the same story. That is also what keeps the host's single
/// session meaningful (the UI still binds one session) and keeps join points aggregating one registry.
/// </para>
/// <para>
/// Only nodes inside a fan-out see this type, and always as <see cref="IRuntimeContext"/>; a linear chain still
/// injects the host's session object itself, which is what
/// <c>EntrySemanticsTests.OneRunSession_IsInjectedAsTheSameInstance_ToEveryNode</c> pins.
/// </para>
/// </summary>
internal sealed class BranchRuntimeContext(IRuntimeContext session) : IRuntimeContext
{
    private readonly IRuntimeContext _session = session;

    /// <summary>
    /// The session this branch belongs to. The engine reads it so a capability configured on the session — the
    /// pause gate, the observer, the retry policy, the error sink, the compensator — keeps working <i>inside</i> a
    /// fan-out: without this, a cast to <see cref="RuntimeContext"/> would come up empty in exactly the place a
    /// wide graph spends its time.
    /// </summary>
    internal IRuntimeContext Session => _session;

    /// <summary>Always false: only the compile phase is true. Not forwarded — it is a constant.</summary>
    public bool IsCompilePhase => false;

    // ── Private to the branch ────────────────────────────────────────────────
    // These are the four a sibling could otherwise overwrite between our await and our node's read.

    /// <inheritdoc />
    public object? Data { get; set; }

    /// <inheritdoc />
    public bool RedirectRequested { get; set; }

    /// <inheritdoc />
    public int? PendingRedirectTarget { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Forwarded to the session, not buffered per branch: the run's lines must read in the order they actually
    /// happened — interleaved branches included — so that a file-backed <see cref="ILogWriter"/> and
    /// <see cref="IRuntimeContext.Logs"/> tell exactly the same story.
    /// </remarks>
    public ObservableCollection<string> Logs
    {
        get => _session.Logs;
        set => _session.Logs = value;
    }

    // ── Forwarded to the session ─────────────────────────────────────────────

    /// <inheritdoc />
    public System.Guid Uid { get => _session.Uid; set => _session.Uid = value; }

    /// <inheritdoc />
    public int Sequence { get => _session.Sequence; set => _session.Sequence = value; }

    /// <inheritdoc />
    public CompileSegment? CurrentEntry { get => _session.CurrentEntry; set => _session.CurrentEntry = value; }

    /// <inheritdoc />
    public int NodeIndex { get => _session.NodeIndex; set => _session.NodeIndex = value; }

    /// <inheritdoc />
    public object? BranchKey { get => _session.BranchKey; set => _session.BranchKey = value; }

    /// <inheritdoc />
    public int Attempt { get => _session.Attempt; set => _session.Attempt = value; }

    /// <inheritdoc />
    public bool IsRunning { get => _session.IsRunning; set => _session.IsRunning = value; }

    /// <inheritdoc />
    public string Status { get => _session.Status; set => _session.Status = value; }

    /// <inheritdoc />
    public int CurrentOrder { get => _session.CurrentOrder; set => _session.CurrentOrder = value; }

    /// <inheritdoc />
    public IWorkflowNodeViewModel? Target { get => _session.Target; set => _session.Target = value; }

    /// <inheritdoc />
    public bool TargetReached { get => _session.TargetReached; set => _session.TargetReached = value; }

    /// <inheritdoc />
    public bool EndedWithError { get => _session.EndedWithError; set => _session.EndedWithError = value; }

    /// <inheritdoc />
    public int? ActiveRedirectTarget { get => _session.ActiveRedirectTarget; set => _session.ActiveRedirectTarget = value; }

    /// <inheritdoc />
    public IWorkflowSlotViewModel? Sender => _session.Sender;

    /// <inheritdoc />
    public IWorkflowSlotViewModel? Receiver => _session.Receiver;

    private int _localSequence;

    /// <summary>
    /// Sequence numbers stay globally unique across branches, so they come from the session.
    /// <para>
    /// Via a cast because <c>Next()</c> — the <c>Interlocked</c> increment — lives on the concrete
    /// <see cref="RuntimeContext"/> and not on the contract; the interface only exposes the <c>Sequence</c>
    /// property, whose read-modify-write two branches could interleave. A session of some other implementation
    /// falls back to a local counter, which costs uniqueness across branches but never duplicates a number.
    /// </para>
    /// </summary>
    public int Next()
        => _session is RuntimeContext concrete ? concrete.Next() : System.Threading.Interlocked.Increment(ref _localSequence);

    /// <inheritdoc />
    public void Log(string entry) => _session.Log(entry);

    /// <inheritdoc />
    /// <remarks>
    /// The line goes through the session — one formatting rule, one sequence counter, one writer — but the
    /// redirect request stays on this branch, so a sibling never sees it.
    /// </remarks>
    public void Error(string message)
    {
        _session.Log($"[Error] {message}");
        RedirectRequested = true;
    }

    /// <inheritdoc />
    public void Warn(string message)
    {
        _session.Log($"[Warning] {message}");
        RedirectRequested = true;
    }

    /// <summary>The node being driven in this branch right now — this branch's own, so siblings cannot overwrite it.</summary>
    internal IWorkflowNodeViewModel? CurrentNode { get; set; }

    /// <inheritdoc />
    public Task ErrorAsync(string message) => ReportAsync(ExecutionReportLevel.Error, message);

    /// <inheritdoc />
    public Task WarnAsync(string message) => ReportAsync(ExecutionReportLevel.Warning, message);

    /// <remarks>
    /// The record is built on the session (one sink, one counter) with <b>this</b> branch's node handed over: the
    /// session's own <c>CurrentNode</c> belongs to whichever branch was driven last. A session that is not the
    /// concrete type has no sink to reach — the line is still written, which is all a custom context ever gets.
    /// </remarks>
    private Task ReportAsync(ExecutionReportLevel level, string message)
    {
        if (level == ExecutionReportLevel.Warning) Warn(message);
        else Error(message);

        return _session is RuntimeContext session
            ? session.ReportNodeAsync(CurrentNode, level, message)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Shared, not private: these are the run's blackboard, documented as readable and writable by nodes, the
    /// engine and the UI alike. Two branches writing the same key concurrently is a race the graph author owns.
    /// </remarks>
    public void Set(string key, object? value) => _session.Set(key, value);

    /// <inheritdoc />
    public bool TryGet(string key, out object? value) => _session.TryGet(key, out value);

    /// <inheritdoc />
    /// <remarks>
    /// Shared: the registry is keyed by source node, each branch touches distinct nodes, and the join point runs
    /// after the whole group — so one registry serves both, and the session's lock covers it.
    /// </remarks>
    public void RegisterOutput(IWorkflowNodeViewModel node, object? value) => _session.RegisterOutput(node, value);

    /// <inheritdoc />
    public void ResetOutputs() => _session.ResetOutputs();

    /// <inheritdoc />
    public IReadOnlyDictionary<IWorkflowNodeViewModel, object?> CollectGroupedInputs(
        System.Collections.Generic.IEnumerable<IWorkflowNodeViewModel> inputNodes)
        => _session.CollectGroupedInputs(inputNodes);
}
