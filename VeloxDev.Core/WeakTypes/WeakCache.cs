using System.Runtime.CompilerServices;

namespace VeloxDev.Core.WeakTypes
{
    public sealed class WeakCache<TTargetKey, TCacheKey> where TTargetKey : class where TCacheKey : class
    {
        private readonly ConditionalWeakTable<TTargetKey, TCacheKey> _caches = new();
        private readonly List<WeakReference<TTargetKey>> _targets = [];
        private readonly object _lock = new();
        private int _counter = 0;

        public int _perceptionThreshold = 4;

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
