namespace VeloxDev.WeakTypes
{
    public sealed class WeakDelegate<TDelegate>
        where TDelegate : Delegate
    {
        private volatile TDelegate? _combinedDelegate;
        private readonly List<WeakReference<Delegate>> _handlers = [];
        private readonly object _lock = new();

        public void AddHandler(TDelegate? handler, bool CanUpdateCache = true)
        {
            lock (_lock)
            {
                if (handler == null) return;
                _handlers.Add(new WeakReference<Delegate>(handler));
                if (CanUpdateCache) RebuildCache();
            }
        }

        public void RemoveHandler(TDelegate? handler, bool CanUpdateCache = true)
        {
            lock (_lock)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i].TryGetTarget(out var target) && target == handler)
                    {
                        _handlers.RemoveAt(i);
                        if (CanUpdateCache) RebuildCache();
                    }
                }
            }
        }

        /// <summary>
        /// Returns the combined delegate (for typed, reflection-free invocation).
        /// The hot path (per-frame InvokeUpdate/InvokeLateUpdate) goes through a lock-free fast lane:
        /// once the combined delegate is cached, it is read directly as volatile — no locking, no DynamicInvoke.
        /// </summary>
        public TDelegate? GetInvocationList()
        {
            var combined = _combinedDelegate;
            if (combined != null) return combined;

            lock (_lock)
            {
                if (_combinedDelegate != null) return _combinedDelegate;
                RebuildCache();
                return _combinedDelegate;
            }
        }

        /// <summary>
        /// Generic invocation (used when the delegate signature is unknown). Retrieves the combined delegate via the
        /// lock-free <see cref="GetInvocationList"/> and then DynamicInvokes it; if the signature is known, use
        /// <see cref="GetInvocationList"/> and invoke it directly in a typed way to avoid reflection overhead.
        /// </summary>
        public void Invoke(object?[] objects)
        {
            GetInvocationList()?.DynamicInvoke(objects);
        }

        private void RebuildCache()
        {
            CleanupCollectedHandlers();

            Delegate? combined = null;
            foreach (var weakRef in _handlers)
            {
                if (weakRef.TryGetTarget(out var handler))
                {
                    combined = Delegate.Combine(combined, handler);
                }
            }

            _combinedDelegate = combined as TDelegate;
        }

        private void CleanupCollectedHandlers()
        {
            for (int i = _handlers.Count - 1; i >= 0; i--)
            {
                if (!_handlers[i].TryGetTarget(out _))
                {
                    _handlers.RemoveAt(i);
                }
            }
        }

        public WeakDelegate<TDelegate> Clone()
        {
            lock (_lock)
            {
                var value = new WeakDelegate<TDelegate>();
                foreach (var weakRef in _handlers)
                {
                    if (weakRef.TryGetTarget(out var handler))
                    {
                        value.AddHandler(handler as TDelegate, CanUpdateCache: false);
                    }
                }
                value._combinedDelegate = value.GetInvocationList();
                return value;
            }
        }
    }
}
