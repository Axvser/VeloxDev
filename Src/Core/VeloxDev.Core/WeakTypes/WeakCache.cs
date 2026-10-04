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
        private readonly ConditionalWeakTable<TTargetKey, TCacheKey> _caches = new();
        private readonly List<WeakReference<TTargetKey>> _targets = [];
        private readonly object _lock = new();
        private int _counter = 0;

        /// <summary>The number of additions after which collected targets are pruned.</summary>
        public int _perceptionThreshold = 4;

        /// <summary>Calls <paramref name="action"/> for every live target and the cache filed under it.</summary>
        public void ForeachCache(Action<TTargetKey, TCacheKey> action)
        {
            lock (_lock)
            {
                _targets.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var weakRef in _targets)
                {
                    if (weakRef.TryGetTarget(out var target))
                    {
                        if (_caches.TryGetValue(target, out var cache))
                        {
                            action(target, cache);
                        }
                    }
                }
            }
        }
        /// <summary>Tries to read the cache filed under <paramref name="target"/>.</summary>
        public bool TryGetCache(TTargetKey target, out TCacheKey? cache)
        {
            lock (_lock)
            {
                if (_caches.TryGetValue(target, out var result))
                {
                    cache = result;
                    return true;
                }
                cache = null;
                return false;
            }
        }
        /// <summary>Files <paramref name="cache"/> under <paramref name="target"/>, replacing any existing entry.</summary>
        public void AddOrUpdate(TTargetKey target, TCacheKey cache)
        {
            lock (_lock)
            {
                if (_counter > _perceptionThreshold)
                {
                    _targets.RemoveAll(w => !w.TryGetTarget(out _));
                    _counter = 0;
                    _perceptionThreshold = GetNextCleanupThreshold(_targets.Count);
                }
                if (_caches.TryGetValue(target, out var result))
                {
                    _caches.Remove(target);
                    _targets.RemoveAll(w => w.TryGetTarget(out var t) && t == target);
                }
                _caches.Add(target, cache);
                _targets.Add(new WeakReference<TTargetKey>(target));
                _counter++;
            }
        }
        /// <summary>Removes the entry filed under <paramref name="target"/>.</summary>
        public void Remove(TTargetKey target)
        {
            lock (_lock)
            {
                if (_caches.TryGetValue(target, out var result))
                {
                    _caches.Remove(target);
                    _targets.RemoveAll(w => w.TryGetTarget(out var t) && t == target);
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
