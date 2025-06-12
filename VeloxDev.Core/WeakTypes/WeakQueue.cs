using System.Collections;

namespace VeloxDev.Core.WeakTypes
{
    /// <summary>
    /// 🧰 > Weak reference queue
    /// <para><see cref="Enqueue(T)"/></para>
    /// <para><see cref="TryDequeue(out T?)"/></para>
    /// <para><see cref="TryPeek(out T?)"/></para>
    /// </summary>
    public sealed class WeakQueue<T> : IEnumerable<T> where T : class
    {
        private readonly Queue<WeakReference<T>> _references = new();
        private readonly object _lock = new();

        public int Count
        {
            get
            {
                if (_references.Count == 0) return 0;

                lock (_lock)
                {
                    Prune();
                    return _references.Count;
                }
            }
        }

        public bool IsEmpty => Count == 0;

        public void Clear()
        {
            lock (_lock)
            {
                _references.Clear();
            }
        }

        public void Enqueue(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            lock (_lock)
            {
                _references.Enqueue(new WeakReference<T>(item));
            }
        }

        public int EnqueueRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            lock (_lock)
            {
                var count = 0;
                foreach (var item in items)
                {
                    if (item != null)
                    {
                        _references.Enqueue(new WeakReference<T>(item));
                        count++;
                    }
                }
                return count;
            }
        }

        public bool TryDequeue(out T? item)
        {
            lock (_lock)
            {
                while (_references.Count > 0)
                {
                    if (_references.Dequeue().TryGetTarget(out item))
                    {
                        return true;
                    }
                }

                item = null;
                return false;
            }
        }

        public bool TryPeek(out T? item)
        {
            lock (_lock)
            {
                while (_references.Count > 0)
                {
                    if (_references.Peek().TryGetTarget(out item))
                    {
                        return true;
                    }
                    _references.Dequeue();
                }

                item = null;
                return false;
            }
        }

        public void TrimExcess()
        {
            lock (_lock)
            {
                Prune();
                _references.TrimExcess();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                Prune();
                foreach (var reference in _references)
                {
                    if (reference.TryGetTarget(out var item))
                    {
                        yield return item;
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void Prune()
        {
            var activeReferences = _references
                .Where(r => r.TryGetTarget(out _))
                .ToList();

            _references.Clear();
            foreach (var reference in activeReferences)
            {
                _references.Enqueue(reference);
            }
        }
    }
}