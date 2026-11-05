using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Merges layout-sync requests from every node that shares an owner — normally one tree — into a single queue, and
/// hands the adapters one place to flush it.
/// </summary>
/// <remarks>
/// <para>
/// A canvas' slot anchors are written back from inside a layout pass, and the triggers for that write-back are
/// spread across several platform events: the whole-tree layout pass, a size change, a model property change, a
/// binding-context change. Left alone, one frame can run the same sync several times per node, and a host that
/// lays out every frame (playback, animation) runs it for nodes that did not move at all.
/// </para>
/// <para>
/// A request is identified by <c>key</c> (the node's view) and collapses with any request already queued for the
/// same key. <see cref="Request(object, WorkflowLayoutStamp, Action)"/> additionally compares the request's
/// <see cref="WorkflowLayoutStamp"/> with the one that produced the last completed run: an unchanged stamp is not
/// queued at all, which is what makes an idle canvas cost nothing during its host's layout passes.
/// </para>
/// <para>
/// The platform supplies exactly one thing: how to move <see cref="Flush"/> onto its UI thread. That delegate must
/// be asynchronous — a platform that runs it inline would clear the queue before the caller returns and no request
/// would ever merge. It returns <see langword="false"/> when the platform could not queue the work, in which case
/// the queue stays pending and the next request tries again.
/// </para>
/// <para>
/// <see cref="Flush"/> runs the queued work on the calling thread, so an adapter that has a trustworthy
/// after-layout, before-render signal can flush synchronously and keep slot anchors on the frame the layout
/// belongs to. <see cref="Schedule"/> is the deferred fallback for platforms or triggers that have no such signal.
/// </para>
/// </remarks>
public sealed class WorkflowLayoutBatch
{
    private static readonly ConditionalWeakTable<object, WorkflowLayoutBatch> Instances = new();

    private readonly object gate = new();
    private readonly Dictionary<object, PendingEntry> pending = new(ReferenceComparer.Instance);
    private readonly ConditionalWeakTable<object, Stamped> applied = new();
    private Func<Action, bool>? post;
    private bool scheduled;

    private WorkflowLayoutBatch() { }

    /// <summary>
    /// Gets the batch shared by everything that passes the same <paramref name="owner"/>, creating it on first use.
    /// </summary>
    /// <param name="owner">
    /// The scope requests merge within — the <see cref="IWorkflowTreeViewModel"/> whose nodes share a UI thread, so a
    /// canvas' nodes all land in one batch. A view may pass itself when it has no tree yet; it then only merges with
    /// its own requests.
    /// </param>
    /// <param name="post">
    /// Moves <see cref="Flush"/> onto the owner's UI thread. The first non-null delegate wins; later calls may omit it.
    /// See the type remarks for the inline-execution and queue-failure contract.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public static WorkflowLayoutBatch For(object owner, Func<Action, bool>? post = null)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));

        var batch = Instances.GetValue(owner, static _ => new WorkflowLayoutBatch());
        if (post is not null)
        {
            // 同一条树上所有节点给的是同一个平台的投递委托；先到的那份胜出，后来的重复赋值不影响行为。
            batch.post ??= post;
        }

        return batch;
    }

    /// <summary>The number of requests queued for the next flush.</summary>
    public int PendingCount
    {
        get { lock (gate) return pending.Count; }
    }

    /// <summary>
    /// Queues <paramref name="work"/> for <paramref name="key"/> unconditionally, replacing any request already
    /// queued for the same key.
    /// </summary>
    /// <param name="key">The request's identity — the view whose layout needs syncing.</param>
    /// <param name="work">The sync to run at the next flush.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="work"/> is <see langword="null"/>.</exception>
    public void Request(object key, Action work)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (work is null) throw new ArgumentNullException(nameof(work));

        lock (gate)
        {
            pending[key] = new PendingEntry(work, hasStamp: false, default);
        }
    }

    /// <summary>
    /// Queues <paramref name="work"/> for <paramref name="key"/> only when <paramref name="stamp"/> differs from the
    /// stamp of the last completed run for that key.
    /// </summary>
    /// <param name="key">The request's identity — the view whose layout needs syncing.</param>
    /// <param name="stamp">The inputs the work depends on, captured cheaply before the work runs.</param>
    /// <param name="work">The sync to run at the next flush.</param>
    /// <returns>
    /// <see langword="true"/> when the request was queued (or already was); <see langword="false"/> when the stamp
    /// proved the work unnecessary.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="work"/> is <see langword="null"/>.</exception>
    public bool Request(object key, WorkflowLayoutStamp stamp, Action work)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (work is null) throw new ArgumentNullException(nameof(work));

        lock (gate)
        {
            if (pending.TryGetValue(key, out var queued))
            {
                // 已有一条强制请求在队里：它本来就代表「必须跑」，不要用指纹条件把它降级。
                if (!queued.HasStamp)
                {
                    return true;
                }

                if (queued.Stamp.Equals(stamp))
                {
                    return true;
                }
            }
            else if (applied.TryGetValue(key, out var last) && last.Stamp.Equals(stamp))
            {
                return false;
            }

            pending[key] = new PendingEntry(work, hasStamp: true, stamp);
            return true;
        }
    }

    /// <summary>Drops any queued request for <paramref name="key"/> and forgets its last stamp.</summary>
    /// <param name="key">The request's identity.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public void Cancel(object key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));

        lock (gate)
        {
            pending.Remove(key);
        }

        applied.Remove(key);
    }

    /// <summary>
    /// Asks the platform to call <see cref="Flush"/> on the owner's UI thread. Repeated calls before that flush
    /// collapse into one.
    /// </summary>
    public void Schedule()
    {
        Func<Action, bool>? deliver;
        lock (gate)
        {
            if (scheduled || pending.Count == 0 || post is null)
            {
                return;
            }

            scheduled = true;
            deliver = post;
        }

        if (!deliver(() => Flush()))
        {
            // 平台没接住这一拍（队列已关闭 / 窗口正在销毁）：把旗标放回去，下一次请求会再排一次。
            lock (gate) scheduled = false;
        }
    }

    /// <summary>
    /// Runs every queued request on the calling thread and empties the queue.
    /// </summary>
    /// <returns><see langword="true"/> when at least one request was queued.</returns>
    /// <remarks>
    /// <para>
    /// The queue is emptied <b>before</b> the work runs, so a request made while a sync is running is a new request
    /// and gets its own turn — it describes state the running sync cannot have seen.
    /// </para>
    /// <para>
    /// A failing request does not cancel the rest: each one is attempted, and the first exception is rethrown after
    /// the queue drains.
    /// </para>
    /// </remarks>
    public bool Flush()
    {
        KeyValuePair<object, PendingEntry>[] snapshot;

        lock (gate)
        {
            if (pending.Count == 0)
            {
                return false;
            }

            snapshot = new KeyValuePair<object, PendingEntry>[pending.Count];
            var index = 0;
            foreach (var pair in pending)
            {
                snapshot[index++] = pair;
            }

            pending.Clear();
            scheduled = false;
        }

        ExceptionDispatchInfo? failure = null;

        foreach (var pair in snapshot)
        {
            try
            {
                pair.Value.Work();

                if (pair.Value.HasStamp)
                {
                    Record(pair.Key).Stamp = pair.Value.Stamp;
                }
                else
                {
                    // 强制请求没有指纹可记：忘掉旧的，下一次带指纹的请求必须再跑一遍才可能跳过。
                    applied.Remove(pair.Key);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
                applied.Remove(pair.Key);
            }
        }

        failure?.Throw();
        return true;
    }

    private readonly struct PendingEntry(Action work, bool hasStamp, WorkflowLayoutStamp stamp)
    {
        public Action Work { get; } = work;

        public bool HasStamp { get; } = hasStamp;

        public WorkflowLayoutStamp Stamp { get; } = stamp;
    }

    // 按 key 的弱引用记录：上一次完整跑完时的指纹。CWT 不持有 key，视图被回收后记录随之消失。
    private Stamped Record(object key) => applied.GetValue(key, static _ => new Stamped());

    private sealed class Stamped
    {
        public WorkflowLayoutStamp Stamp { get; set; }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
