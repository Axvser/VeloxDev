﻿namespace VeloxDev.Core.WeakTypes
{
    /// <summary>
    /// 🧰 > Weak reference delegate
    /// <para><see cref="AddHandler(TDelegate)"/></para>
    /// <para><see cref="RemoveHandler(TDelegate)"/></para>
    /// <para><see cref="GetInvocationList"/> - Release invalid elements and return valid TDelegate </para>
    /// </summary>
    public sealed class WeakDelegate<TDelegate> : ICloneable where TDelegate : Delegate
    {
        private TDelegate? _combinedDelegate;
        private readonly List<WeakReference<Delegate>> _handlers = [];
        private readonly object _lock = new();

        public void AddHandler(TDelegate? handler)
        {
            lock (_lock)
            {
                if (handler == null) return;
                _handlers.Add(new WeakReference<Delegate>(handler));
                _combinedDelegate = null;
            }
        }

        public void RemoveHandler(TDelegate? handler)
        {
            lock (_lock)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i].TryGetTarget(out var target) && target == handler)
                    {
                        _handlers.RemoveAt(i);
                        _combinedDelegate = null;
                    }
                }
            }
        }

        public TDelegate? GetInvocationList()
        {
            lock (_lock)
            {
                if (_combinedDelegate != null) return _combinedDelegate;

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
                return _combinedDelegate;
            }
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

        public void Invoke(object?[] objects)
        {
            lock (_lock)
            {
                foreach (var weakref in _handlers)
                {
                    if (weakref.TryGetTarget(out var del))
                    {
                        del.DynamicInvoke(objects);
                    }
                }
            }
        }

        public object Clone()
        {
            lock (_lock)
            {
                return DeepCopy();
            }
        }

        public WeakDelegate<TDelegate> DeepCopy()
        {
            lock (_lock)
            {
                var value = new WeakDelegate<TDelegate>();
                foreach (var weakRef in _handlers)
                {
                    value._handlers.Add(weakRef);
                }
                return value;
            }
        }
    }
}
