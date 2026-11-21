using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The shared execution flow of a view pool: it keeps one host element's views in sync with a
/// visible-item collection, materializes each item at most once, recycles views by runtime type, and
/// dispatches creation through <see cref="IViewScheduler{TPriorityCore}.Schedule"/> in small
/// batches.
/// </summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <typeparam name="TPriorityCore">The platform's dispatcher-priority type, or <c>NonPriority</c>.</typeparam>
/// <remarks>
/// <para>
/// This is the platform-neutral half of view materialization. It owns the pool, the active map, the
/// pending queue and the batch loop; every platform-specific step is delegated to the host's layers.
/// </para>
/// <para>
/// Collection notifications and the pool's public members must be used on the host's thread. The pool
/// does not marshal by itself; <see cref="IViewScheduler{TPriorityCore}"/> is the only boundary
/// where work is handed to that thread.
/// </para>
/// </remarks>
public abstract class ViewPoolCore<THost, TView, TItem, TPriorityCore> : IDisposable
    where THost : class
    where TView : class
    where TItem : class
{
    private readonly THost hostElement;
    private readonly IViewHost<THost, TView, TItem, TPriorityCore> host;
    private readonly INotifyCollectionChanged source;
    private readonly IEnumerable<object> sourceItems;
    private readonly Dictionary<Type, Queue<TView>> pools = [];
    private readonly Dictionary<object, TView> active = new(ViewReferenceComparer<object>.Instance);
    private readonly Queue<object> pending = new();
    private readonly HashSet<object> pendingSet = new(ViewReferenceComparer<object>.Instance);
    private readonly HashSet<Type> unsupportedTypes = [];
    private int createdViewCount;
    private int reusedViewCount;
    private int destroyedViewCount;
    private bool scheduled;
    private bool disposed;

    /// <summary>Creates a pool and starts tracking <paramref name="source"/>.</summary>
    /// <param name="hostElement">The host element the host materializes views into.</param>
    /// <param name="host">The composed platform layers.</param>
    /// <param name="source">The visible-item collection, usually <see cref="ITreeHelper.VisibleItems"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> cannot be enumerated.</exception>
    protected ViewPoolCore(
        THost hostElement,
        IViewHost<THost, TView, TItem, TPriorityCore> host,
        INotifyCollectionChanged source)
    {
        this.hostElement = hostElement ?? throw new ArgumentNullException(nameof(hostElement));
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.source = source ?? throw new ArgumentNullException(nameof(source));

        if (source is not IEnumerable enumerable)
        {
            throw new ArgumentException("The visible-item source must be enumerable.", nameof(source));
        }

        sourceItems = enumerable.Cast<object>();
        this.source.CollectionChanged += OnCollectionChanged;
        SeedFromSource();
        if (host.LinkLayer.IsActive)
        {
            RenderLinks();
        }
    }

    /// <summary>Gets the number of items that currently have a bound view.</summary>
    public int ActiveViewCount => active.Count;

    /// <summary>Gets the number of items waiting for a scheduled materialization batch.</summary>
    public int PendingViewCount => pendingSet.Count;

    /// <summary>Gets the cumulative number of views the factory created for this pool.</summary>
    public int CreatedViewCount => createdViewCount;

    /// <summary>Gets the cumulative number of views taken from the recycle pool.</summary>
    public int ReusedViewCount => reusedViewCount;

    /// <summary>Gets the cumulative number of views destroyed because the pool policy refused to keep them.</summary>
    public int DestroyedViewCount => destroyedViewCount;

    /// <summary>Gets the number of views currently held for reuse.</summary>
    public int RecycledViewCount
    {
        get
        {
            var count = 0;
            foreach (var pool in pools.Values)
            {
                count += pool.Count;
            }

            return count;
        }
    }

    /// <summary>Stops tracking the visible set and releases every active view back to the host.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        source.CollectionChanged -= OnCollectionChanged;
        RecycleAll();
        pending.Clear();
        pendingSet.Clear();
        pools.Clear();
        unsupportedTypes.Clear();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (disposed)
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AddItems(e.NewItems);
                break;

            case NotifyCollectionChangedAction.Remove:
                RemoveItems(e.OldItems);
                break;

            case NotifyCollectionChangedAction.Replace:
                RemoveItems(e.OldItems);
                AddItems(e.NewItems);
                break;

            case NotifyCollectionChangedAction.Reset:
                RecycleAll();
                pending.Clear();
                pendingSet.Clear();
                SeedFromSource();
                break;

            // Move only changes enumeration order; the pool's view identity is the item reference.
            case NotifyCollectionChangedAction.Move:
                break;
        }

        if (host.LinkLayer.IsActive)
        {
            RenderLinks();
        }
    }

    private void AddItems(IList? items)
    {
        if (items is null)
        {
            return;
        }

        var changed = false;
        foreach (var item in items)
        {
            changed |= AddPending(item);
        }

        if (changed)
        {
            ScheduleNextBatch();
        }
    }

    private void RemoveItems(IList? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item is not null)
            {
                pendingSet.Remove(item);
            }

            Recycle(item);
        }
    }

    private void SeedFromSource()
    {
        foreach (var item in sourceItems)
        {
            AddPending(item);
        }

        ScheduleNextBatch();
    }

    private bool AddPending(object? item)
    {
        if (item is not TItem typedItem ||
            active.ContainsKey(item) ||
            unsupportedTypes.Contains(item.GetType()) ||
            host.LinkLayer.Handles(typedItem))
        {
            return false;
        }

        if (!pendingSet.Add(item))
        {
            return false;
        }

        pending.Enqueue(item);
        return true;
    }

    private void RenderLinks()
    {
        if (disposed || !host.LinkLayer.IsActive)
        {
            return;
        }

        List<TItem>? links = null;
        foreach (var item in sourceItems)
        {
            if (item is TItem typedItem && host.LinkLayer.Handles(typedItem))
            {
                links ??= [];
                links.Add(typedItem);
            }
        }

        try
        {
            host.LinkLayer.Render(hostElement, links ?? (IReadOnlyList<TItem>)Array.Empty<TItem>());
        }
        catch (Exception exception)
        {
            host.Diagnostics.Failed(null, ViewStage.Link, exception);
        }
    }

    private void ScheduleNextBatch()
    {
        if (disposed || scheduled || pendingSet.Count == 0)
        {
            return;
        }

        scheduled = true;
        var scheduler = host.Scheduler;
        if (!scheduler.Schedule(ProcessNextBatch, scheduler.BatchPriority))
        {
            scheduled = false;
            host.Diagnostics.Failed(null, ViewStage.Schedule, null);
        }
    }

    private void ProcessNextBatch()
    {
        scheduled = false;
        if (disposed)
        {
            return;
        }

        var processed = 0;
        var batchSize = Math.Max(1, host.PoolPolicy.BatchSize);
        var budgetMilliseconds = host.PoolPolicy.FrameBudgetMilliseconds;
        var budgetTicks = budgetMilliseconds > 0d
            ? (long)(budgetMilliseconds * Stopwatch.Frequency / 1000d)
            : 0L;
        var started = Stopwatch.GetTimestamp();

        while (processed < batchSize && pending.Count > 0)
        {
            var item = pending.Dequeue();
            // Removed while it was still queued: the tombstone is simply skipped.
            if (!pendingSet.Remove(item))
            {
                continue;
            }

            Materialize(item);
            processed++;

            if (budgetTicks > 0L && Stopwatch.GetTimestamp() - started >= budgetTicks)
            {
                break;
            }
        }

        if (host.PoolPolicy is IViewBatchFeedback feedback)
        {
            feedback.OnBatchCompleted(processed, Stopwatch.GetTimestamp() - started, pendingSet.Count);
        }

        if (pendingSet.Count > 0)
        {
            ScheduleNextBatch();
        }
    }

    private void Materialize(object item)
    {
        if (item is not TItem typedItem || active.ContainsKey(item))
        {
            return;
        }

        var itemType = item.GetType();
        if (unsupportedTypes.Contains(itemType) ||
            host.LinkLayer.Handles(typedItem) ||
            !host.Factory.WantsItem(typedItem))
        {
            return;
        }

        TView? view = null;
        var stage = ViewStage.Create;
        try
        {
            if (pools.TryGetValue(itemType, out var pool) && pool.Count > 0)
            {
                view = pool.Dequeue();
                reusedViewCount++;
            }

            if (view is null)
            {
                view = host.Factory.CreateView(typedItem);
                if (view is null)
                {
                    unsupportedTypes.Add(itemType);
                    return;
                }

                createdViewCount++;
            }

            stage = ViewStage.Activate;
            host.Container.Activate(view, typedItem);
            stage = ViewStage.Attach;
            host.Container.Attach(hostElement, view);
            stage = ViewStage.Layer;
            host.Container.SetZOrder(view, host.LayerPolicy.GetZOrder(typedItem));
            stage = ViewStage.Arrange;
            host.Arranger.Arrange(view, typedItem);
            stage = ViewStage.Measure;
            host.MeasureHost.Measure(hostElement, view, typedItem);
            active[item] = view;
        }
        catch (Exception exception)
        {
            if (view is not null)
            {
                TryRelease(typedItem, view);
                ReturnToPool(itemType, view);
            }

            host.Diagnostics.Failed(typedItem, stage, exception);
        }
    }

    private void Recycle(object? item)
    {
        if (item is null || !active.TryGetValue(item, out var view))
        {
            return;
        }

        active.Remove(item);
        if (item is TItem typedItem)
        {
            TryRelease(typedItem, view);
        }

        ReturnToPool(item.GetType(), view);
    }

    private void ReturnToPool(Type itemType, TView view)
    {
        var pool = PoolOf(itemType);
        var max = host.PoolPolicy.MaxRecycledViewsPerType;
        if (max <= 0 || pool.Count < max)
        {
            pool.Enqueue(view);
            return;
        }

        try
        {
            host.Container.Destroy(hostElement, view);
            destroyedViewCount++;
        }
        catch (Exception exception)
        {
            host.Diagnostics.Failed(null, ViewStage.Destroy, exception);
        }
    }

    private void RecycleAll()
    {
        if (active.Count == 0)
        {
            return;
        }

        foreach (var item in active.Keys.ToArray())
        {
            Recycle(item);
        }
    }

    private void TryRelease(TItem item, TView view)
    {
        var stage = ViewStage.Deactivate;
        try
        {
            host.Container.Deactivate(view);
            stage = ViewStage.Detach;
            host.Container.Detach(hostElement, view);
        }
        catch (Exception exception)
        {
            host.Diagnostics.Failed(item, stage, exception);
        }
    }

    private Queue<TView> PoolOf(Type itemType)
    {
        if (!pools.TryGetValue(itemType, out var pool))
        {
            pool = [];
            pools[itemType] = pool;
        }

        return pool;
    }

}

/// <summary>The default view pool: <see cref="ViewPoolCore{THost,TView,TItem,TPriorityCore}"/> with no further specialization.</summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <typeparam name="TPriorityCore">The platform's dispatcher-priority type, or <c>NonPriority</c>.</typeparam>
public sealed class ViewPool<THost, TView, TItem, TPriorityCore>
    : ViewPoolCore<THost, TView, TItem, TPriorityCore>
    where THost : class
    where TView : class
    where TItem : class
{
    /// <summary>Creates a pool and starts tracking <paramref name="source"/>.</summary>
    /// <param name="hostElement">The host element the host materializes views into.</param>
    /// <param name="host">The composed platform layers.</param>
    /// <param name="source">The visible-item collection, usually <see cref="ITreeHelper.VisibleItems"/>.</param>
    public ViewPool(
        THost hostElement,
        IViewHost<THost, TView, TItem, TPriorityCore> host,
        INotifyCollectionChanged source)
        : base(hostElement, host, source)
    {
    }
}

// 池的 active 表按引用身份比较：ViewModel 不重定义 Equals，同类型的不同实例不能互相顶替。
internal sealed class ViewReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly ViewReferenceComparer<T> Instance = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    // 用 RuntimeHelpers 的 hash，避免 ViewModel 自己的 GetHashCode override 把两个实例算成同一个桶键。
    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
