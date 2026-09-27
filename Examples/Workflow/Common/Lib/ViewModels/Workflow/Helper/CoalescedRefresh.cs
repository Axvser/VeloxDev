using System.Threading;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// Merges a burst of "the surface is stale, refresh it" requests into one.
/// </summary>
/// <remarks>
/// <para>
/// Every Agent tool call asks for a refresh, and a refresh of this surface is <b>total</b>: it resolves the named
/// controls again, runs a layout pass and recomputes the visible region. A single Agent turn makes dozens of tool
/// calls, so the editor spent the whole turn doing dozens of full refreshes — measured by the user as "the node
/// editor responds very very slowly while the conversation is running" (2026-09-27).
/// </para>
/// <para>
/// The work is idempotent, so requests made while one is already queued mean nothing beyond the first: the refresh
/// about to run will see the state they were asking about. The flag is cleared <b>before</b> the refresh runs rather
/// than after, so a change made *during* the refresh asks for one more turn — that one is what represents the state
/// after it.
/// </para>
/// <para>
/// Kept platform-neutral by taking the thread hop as a delegate: each demo's surface lives on its own UI thread, and
/// that is the one thing they do differently.
/// </para>
/// </remarks>
public sealed class CoalescedRefresh
{
    private readonly Action _refresh;
    private readonly Action<Action> _postToSurfaceThread;
    private int _queued;

    /// <summary>Creates the coalescer.</summary>
    /// <param name="refresh">The work, run on the surface's own thread at most once per burst.</param>
    /// <param name="postToSurfaceThread">
    /// Schedules a delegate on that thread — a dispatcher hop, in every demo that uses this. <b>It has to be
    /// asynchronous:</b> running the delegate inline would clear the queued flag before the caller returns, so two
    /// requests in the same turn would both do the work and nothing would be merged.
    /// </param>
    public CoalescedRefresh(Action refresh, Action<Action> postToSurfaceThread)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _postToSurfaceThread = postToSurfaceThread ?? throw new ArgumentNullException(nameof(postToSurfaceThread));
    }

    /// <summary>Whether a refresh is queued but has not run yet.</summary>
    public bool IsQueued => Volatile.Read(ref _queued) == 1;

    /// <summary>
    /// Asks for a refresh. The first request in a burst does the work; the ones behind it fall in with it.
    /// </summary>
    public void Request()
    {
        if (Interlocked.CompareExchange(ref _queued, 1, 0) != 0) return;

        _postToSurfaceThread(() =>
        {
            Volatile.Write(ref _queued, 0);
            _refresh();
        });
    }
}
