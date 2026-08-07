using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace VeloxDev.MVVM;

/// <summary>
/// Ensures that <see cref="INotifyCollectionChanged.CollectionChanged"/> events
/// on ObservableCollection properties are subscribed even when the backing field
/// is initialized directly (e.g. <c>= []</c>), bypassing the generated setter.
///
/// Uses <see cref="ConditionalWeakTable{TKey,TValue}"/> for weak-reference tracking:
/// once the collection is collected, its entry is automatically removed — no leaks.
/// Thread-safe for concurrent getter/setter access.
/// </summary>
public static class ObservableCollectionTracker
{
    private static readonly ConditionalWeakTable<object, Entry> _table = new();

    /// <summary>
    /// Called from the generated property getter. If <paramref name="collection"/>
    /// has not yet been subscribed for <paramref name="handler"/>, subscribes it.
    /// Subsequent calls are a fast O(1) lookup.
    /// </summary>
    public static void EnsureSubscribed(
        object? collection,
        NotifyCollectionChangedEventHandler handler)
    {
        if (collection is not INotifyCollectionChanged)
            return;

        var entry = _table.GetOrCreateValue(collection!);
        if (entry.TryAdd(handler))
        {
            ((INotifyCollectionChanged)collection).CollectionChanged += handler;
        }
    }

    /// <summary>
    /// Called from the generated property setter when a collection is replaced.
    /// Unsubscribes the handler from the old collection value and removes its
    /// tracking entry so the subscription is not accidentally restored later.
    /// </summary>
    public static void Unsubscribe(
        object? collection,
        NotifyCollectionChangedEventHandler handler)
    {
        if (collection is not INotifyCollectionChanged)
            return;

        ((INotifyCollectionChanged)collection).CollectionChanged -= handler;
        if (_table.TryGetValue(collection!, out var entry))
        {
            entry.Remove(handler);
        }
    }

    /// <summary>
    /// Per-collection tracked handlers. Must be a reference type so that
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> can key by collection identity.
    /// </summary>
    private sealed class Entry
    {
        // Dedupe by (Method, Target) identity, not by delegate reference identity.
        // The generated getters pass a method group (e.g. OnItemsCollectionChanged),
        // which produces a FRESH delegate instance on every getter access. Comparing
        // by reference would therefore re-subscribe on every access and grow the
        // event's invocation list without bound. Comparing by method + target makes
        // the subscription idempotent across getter accesses while still treating
        // genuinely distinct handlers (different methods, or the same method bound
        // to different target instances) as different.
        private readonly HashSet<Delegate> _handlers = new(MethodTargetEqualityComparer.Instance);

        public bool TryAdd(Delegate handler)
        {
            lock (_handlers)
            {
                return _handlers.Add(handler);
            }
        }

        public void Remove(Delegate handler)
        {
            lock (_handlers)
            {
                _handlers.Remove(handler);
            }
        }
    }

    /// <summary>
    /// Compares delegates by (Method, Target) identity: the same method bound to
    /// the same target instance is the same handler. Target is compared by
    /// reference (not MulticastDelegate equality) so value-equal-but-distinct
    /// target instances are never conflated.
    /// </summary>
    private sealed class MethodTargetEqualityComparer : IEqualityComparer<Delegate>
    {
        public static readonly MethodTargetEqualityComparer Instance = new();

        public bool Equals(Delegate? x, Delegate? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.Method == y.Method && ReferenceEquals(x.Target, y.Target);
        }

        public int GetHashCode(Delegate obj)
        {
            var hash = obj.Method.GetHashCode();
            if (obj.Target is not null)
                hash = (hash * 397) ^ RuntimeHelpers.GetHashCode(obj.Target);
            return hash;
        }
    }
}
