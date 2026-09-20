using System.Threading;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// The call counters one scope's tool budgets are enforced against, chained to the counters of the scope
/// that spawned it.
/// <para>
/// The chain is what makes a spawned sub-agent's allowance a <i>share of</i> its parent's rather than a
/// second pot beside it. <see cref="Spend"/> walks the chain, so the outermost ledger's total is the number
/// of calls made anywhere in the tree — which is what makes an arbitrarily deep tree terminate: depth is
/// bounded by the root's allowance because the root's allowance is the only counter that has to run out.
/// Two independent pots would leave a parent able to hand every one of its children its own full remainder,
/// and the tree's total spend unbounded.
/// </para>
/// <para>
/// <b>A root scope's ledger has no <see cref="Outer"/>, and then every member below reduces to exactly the
/// three counters this replaced.</b> A host that never attaches a sub-agent scope cannot observe any
/// difference — which is the property that keeps the existing budget tests meaningful.
/// </para>
/// </summary>
/// <param name="owner">The scope whose caps govern <paramref name="outer"/>-relative accounting at this level.</param>
/// <param name="outer">
/// The ledger this one also draws on, or <c>null</c> for a scope that owns the session's allowance.
/// </param>
internal sealed class ToolCallLedger(WorkflowAgentScope owner, ToolCallLedger? outer = null)
{
    private int _total;
    private int _read;
    private int _write;

    /// <summary>
    /// The scope this ledger belongs to. Read for its caps, never for its state: a host is free to call
    /// <c>WithMaxToolCalls</c> after the toolkit exists, and the refusal has to see the new value.
    /// </summary>
    public WorkflowAgentScope Owner { get; } = owner;

    /// <summary>The ledger this one draws on as well, or <c>null</c> when this scope owns the allowance.</summary>
    public ToolCallLedger? Outer { get; } = outer;

    /// <summary>
    /// The ledger at the top of the chain — the session's shared allowance. Equals this instance for a scope
    /// nobody spawned, which is how callers tell "my own budget" from "the tree's budget" without a flag.
    /// </summary>
    public ToolCallLedger Root => Outer?.Root ?? this;

    /// <summary>
    /// Counts one completed call here <i>and at every level above</i>. Called only for the counters, never
    /// for a tool that manages the budget rather than spending it.
    /// </summary>
    /// <param name="isQuery">Whether the call is a read-only query, which is charged to the read counter.</param>
    public void Spend(bool isQuery)
    {
        Interlocked.Increment(ref _total);
        if (isQuery)
            Interlocked.Increment(ref _read);
        else
            Interlocked.Increment(ref _write);

        // Walk rather than snapshot: a scope's own total must include everything its subtree spent, or the
        // sub-limit a host granted would never be reached while a child quietly spent the tree's allowance.
        Outer?.Spend(isQuery);
    }

    /// <summary>
    /// What this level has spent, including everything below it. Read without a lock — the counters are only
    /// ever moved by <see cref="Interlocked"/>, so a torn read is not possible and a stale one is harmless.
    /// </summary>
    public (int ToolCalls, int ReadCalls, int WriteCalls) Usage
        => (Volatile.Read(ref _total), Volatile.Read(ref _read), Volatile.Read(ref _write));

    /// <summary>
    /// Zeroes this level and every level above it. The whole chain, not this level alone: a budget is
    /// reopened by the user's agreement, and clearing only the innermost counter would leave the model just
    /// as refused as before, by an ancestor it cannot see.
    /// </summary>
    public void ResetChain()
    {
        Interlocked.Exchange(ref _total, 0);
        Interlocked.Exchange(ref _read, 0);
        Interlocked.Exchange(ref _write, 0);
        Outer?.ResetChain();
    }
}
