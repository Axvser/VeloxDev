using System.Collections;

namespace VeloxDev.WeakTypes
{
    /// <summary>A FIFO queue that holds its items weakly; entries whose target has been collected are pruned.</summary>
    public sealed class WeakQueue<T> : IEnumerable<T> where T : class
    {
        private readonly Queue<WeakReference<T>> _references = new();
        private readonly object _lock = new();

        /// <summary>The number of live items in the queue.</summary>
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

        /// <summary>Whether the queue holds no live items.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>Removes every entry.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _references.Clear();
            }
        }

        /// <summary>Adds <paramref name="item"/> to the back of the queue.</summary>
        public void Enqueue(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            lock (_lock)
            {
                _references.Enqueue(new WeakReference<T>(item));
            }
        }

        /// <summary>Adds every non-null item in <paramref name="items"/> to the back of the queue.</summary>
        /// <returns>How many items were added.</returns>
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

        /// <summary>Removes and returns the item at the front of the queue.</summary>
        /// <returns><see langword="true"/> when a live item was found.</returns>
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

        /// <summary>Returns the item at the front of the queue without removing it.</summary>
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
                    _references.Dequeue();
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

        /// <summary>Enumerates the live items from front to back.</summary>
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
