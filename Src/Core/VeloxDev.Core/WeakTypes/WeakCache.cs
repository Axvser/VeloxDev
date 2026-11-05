using System.Runtime.CompilerServices;
#if NET
using System.Diagnostics.CodeAnalysis;
#endif

namespace VeloxDev.WeakTypes
{
    /// <summary>
    /// A keyed cache whose entries die with the targets they were filed under.
    /// </summary>
    /// <typeparam name="TTargetKey">The object an entry is filed under; an entry never keeps it alive.</typeparam>
    /// <typeparam name="TCacheKey">What is filed under a target, kept alive only while that target is.</typeparam>
    /// <remarks>
    /// <typeparamref name="TCacheKey"/> carries <c>DynamicallyAccessedMembers</c> only to keep
    /// <see cref="ConditionalWeakTable{TKey, TValue}"/>'s annotation satisfied for the trimmer: that requirement
    /// serves <c>GetOrCreateValue</c>, which this type never calls. Concrete type arguments satisfy it without a
    /// public parameterless constructor.
    /// </remarks>
    public sealed class WeakCache<
        TTargetKey,
#if NET
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
#endif
        TCacheKey>
        where TTargetKey : class where TCacheKey : class
    {
        // 条目自带登记进 _targets 的那个 WeakReference，更新/删除才能 O(1) 命中而不必扫表。
        // HashSet 依赖 WeakReference 的引用相等。
        private sealed class Entry
        {
            public WeakReference<TTargetKey> Target = null!;
            public TCacheKey Value = null!;
        }

        private readonly ConditionalWeakTable<TTargetKey, Entry> _caches = new();
        private readonly HashSet<WeakReference<TTargetKey>> _targets = [];
        private readonly object _lock = new();
        private int _counter = 0;
        private int _perceptionThreshold = 4;

        /// <summary>Calls <paramref name="action"/> for every live target and the cache filed under it.</summary>
        /// <remarks>The callback runs outside the internal lock, so it may read or write this cache.</remarks>
        public void ForeachCache(Action<TTargetKey, TCacheKey> action)
        {
            List<KeyValuePair<TTargetKey, TCacheKey>> snapshot;
            lock (_lock)
            {
                _targets.RemoveWhere(w => !w.TryGetTarget(out _));
                snapshot = new(_targets.Count);
                foreach (var weakRef in _targets)
                {
                    if (weakRef.TryGetTarget(out var target) && _caches.TryGetValue(target, out var entry))
                    {
                        snapshot.Add(new(target, entry.Value));
                    }
                }
            }
            foreach (var pair in snapshot)
            {
                action(pair.Key, pair.Value);
            }
        }
        /// <summary>Tries to read the cache filed under <paramref name="target"/>.</summary>
        public bool TryGetCache(TTargetKey target, out TCacheKey? cache)
        {
            // CWT 自身线程安全，读不进锁；写仍串行在 _lock 上，以保持 _targets 同步。
            if (_caches.TryGetValue(target, out var entry))
            {
                cache = Volatile.Read(ref entry.Value);
                return true;
            }
            cache = null;
            return false;
        }
        /// <summary>Files <paramref name="cache"/> under <paramref name="target"/>, replacing any existing entry.</summary>
        public void AddOrUpdate(TTargetKey target, TCacheKey cache)
        {
            lock (_lock)
            {
                if (_caches.TryGetValue(target, out var existing))
                {
                    Volatile.Write(ref existing.Value, cache);
                    return;
                }
                if (_counter > _perceptionThreshold)
                {
                    _targets.RemoveWhere(w => !w.TryGetTarget(out _));
                    _counter = 0;
                    _perceptionThreshold = GetNextCleanupThreshold(_targets.Count);
                }
                var entry = new Entry { Target = new WeakReference<TTargetKey>(target), Value = cache };
                _caches.Add(target, entry);
                _targets.Add(entry.Target);
                _counter++;
            }
        }
        /// <summary>Removes the entry filed under <paramref name="target"/>.</summary>
        public void Remove(TTargetKey target)
        {
            lock (_lock)
            {
                if (_caches.TryGetValue(target, out var entry))
                {
                    _caches.Remove(target);
                    _targets.Remove(entry.Target);
                }
            }
        }
        private static int GetNextCleanupThreshold(int currentCount)
        {
            int nextCapacity = currentCount == 0 ? 4 : currentCount * 2;
            return (int)(nextCapacity * 0.9);
        }
    }
}
