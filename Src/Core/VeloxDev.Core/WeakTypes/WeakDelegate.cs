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
        /// 返回组合后的委托（用于类型化、无反射的调用）。
        /// 热路径（每帧 InvokeUpdate/InvokeLateUpdate）走无锁快速通道：
        /// 一旦缓存了组合委托，直接 volatile 读取，不再加锁、不再 DynamicInvoke。
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
        /// 通用调用（未知委托签名时使用）。经无锁 <see cref="GetInvocationList"/> 取出组合委托再 DynamicInvoke；
        /// 若已知签名，请改用 <see cref="GetInvocationList"/> 后直接类型化调用，避免反射开销。
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
