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
        private readonly HashSet<Delegate> _handlers = new(ReferenceEqualityComparer.Instance);

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
    /// Ensures delegates are compared by reference identity, not by
    /// MulticastDelegate equality (which can match different lambdas).
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<Delegate>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(Delegate? x, Delegate? y) => ReferenceEquals(x, y);
        public int GetHashCode(Delegate obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
