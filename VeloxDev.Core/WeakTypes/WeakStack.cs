using System.Collections;

namespace VeloxDev.Core.WeakTypes
{
    public sealed class WeakStack<T> : IEnumerable<T> where T : class
    {
        private readonly Stack<WeakReference<T>> _references = new();
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

        public void Push(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            lock (_lock)
            {
                _references.Push(new WeakReference<T>(item));
            }
        }

        public int PushRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            lock (_lock)
            {
                var count = 0;
                foreach (var item in items.Reverse())
                {
                    if (item != null)
                    {
                        _references.Push(new WeakReference<T>(item));
                        count++;
                    }
                }
                return count;
            }
        }

        public bool TryPop(out T? item)
        {
            lock (_lock)
            {
                while (_references.Count > 0)
                {
                    if (_references.Pop().TryGetTarget(out item))
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
                    _references.Pop();
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
            activeReferences.Reverse();

            _references.Clear();
            foreach (var reference in activeReferences)
            {
                _references.Push(reference);
            }
        }
    }
}