namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The policy that bounds the pool: how many views one materialization batch may create, and how many
/// recycled views one type may keep.
/// </summary>
/// <remarks>
/// A non-positive <see cref="MaxRecycledViewsPerType"/> means unlimited. The policy is platform
/// configuration, not a platform primitive; the pool applies it without asking the adapter.
/// </remarks>
public interface IViewPoolPolicy
{
    /// <summary>Gets the maximum number of views one scheduled batch may materialize; values below one are treated as one.</summary>
    int BatchSize { get; }

    /// <summary>Gets the wall-clock budget for one batch in milliseconds; zero or negative means no time limit.</summary>
    /// <remarks>
    /// The pool stops a batch when either <see cref="BatchSize"/> is reached or this budget expires, then
    /// schedules the remaining work on the next dispatcher turn. A budget keeps one large viewport from
    /// monopolizing the UI thread while still cutting dispatcher round-trips.
    /// </remarks>
    double FrameBudgetMilliseconds { get; }

    /// <summary>Gets the maximum number of recycled views kept per runtime type; zero or negative means unlimited.</summary>
    int MaxRecycledViewsPerType { get; }
}
