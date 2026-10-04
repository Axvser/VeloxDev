using System.Collections;

namespace VeloxDev.WeakTypes
{
    /// <summary>A LIFO stack that holds its items weakly; entries whose target has been collected are pruned.</summary>
    public sealed class WeakStack<T> : IEnumerable<T> where T : class
    {
        private readonly Stack<WeakReference<T>> _references = new();
        private readonly object _lock = new();

        /// <summary>The number of live items in the stack.</summary>
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

        /// <summary>Whether the stack holds no live items.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>Removes every entry.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _references.Clear();
            }
        }

        /// <summary>Pushes <paramref name="item"/> onto the top of the stack.</summary>
        public void Push(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            lock (_lock)
            {
                _references.Push(new WeakReference<T>(item));
            }
        }

        /// <summary>Pushes every non-null item in <paramref name="items"/>, preserving their order on the stack.</summary>
        /// <returns>How many items were pushed.</returns>
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

        /// <summary>Removes and returns the item on top of the stack.</summary>
        /// <returns><see langword="true"/> when a live item was found.</returns>
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

        /// <summary>Returns the item on top of the stack without removing it.</summary>
        /// <returns><see langword="true"/> when a live item was found.</returns>
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

        /// <summary>Prunes collected entries and releases unused capacity.</summary>
        public void TrimExcess()
        {
            lock (_lock)
            {
                Prune();
                _references.TrimExcess();
            }
        }

        /// <summary>Enumerates the live items from top to bottom.</summary>
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
