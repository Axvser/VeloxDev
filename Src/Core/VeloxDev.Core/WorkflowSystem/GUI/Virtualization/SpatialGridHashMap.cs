using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A generic spatial hash map that supports any type implementing <see cref="ISpatialBoundsProvider"/>.
/// Automatically tracks bounds changes and updates the spatial index accordingly.
/// Also tracks the minimal bounds covering all registered items via <see cref="Bounds"/>.
/// </summary>
/// <typeparam name="T">The type of elements to store, must implement <see cref="ISpatialBoundsProvider"/>.</typeparam>
public class SpatialGridHashMap<T>(double cellSize) : ISpatialMap<T>
    where T : class, ISpatialBoundsProvider
{
    private readonly Dictionary<CellKey, HashSet<T>> _grid = [];
    private readonly Dictionary<T, Viewport> _trackedItems = [];
    private readonly double _cellSize = Math.Max(1d, cellSize);
    private readonly HashSet<T> _queryScratch = [];
    private Viewport _bounds;
    private bool _boundsDirty;
    private bool _reindexing;
    private bool _rerunPending;

    public Viewport Bounds
    {
        get
        {
            EnsureBounds();
            return _bounds;
        }
    }

    private void EnsureBounds()
    {
        if (!_boundsDirty) return;
        _boundsDirty = false;

        if (_trackedItems.Count == 0)
        {
            _bounds = Viewport.Empty;
            return;
        }

        var union = Viewport.Empty;
        foreach (var b in _trackedItems.Values)
            union = Viewport.Union(union, b);
        _bounds = union;
    }

    private void InvalidateBounds()
    {
        _boundsDirty = true;
        EnsureBounds();
    }

    public void Insert(T item)
    {
        if (item == null) return;

        if (_trackedItems.ContainsKey(item)) return;

        var b = item.Bounds;
        RegisterItem(item, b);
        // Items with empty/NaN bounds are registered for change tracking but NOT indexed
        // until bounds become meaningful. OnItemPropertyChanged handles the transition
        // when PropertyChanged fires after the view layer positions the item.
        if (!b.IsEmpty)
            IndexItem(item, b);
        InvalidateBounds();
    }

    public void Remove(T item)
    {
        if (item == null || !_trackedItems.TryGetValue(item, out var b)) return;

        UnregisterItem(item);
        if (!b.IsEmpty)
            DeindexItem(item, b);
        _trackedItems.Remove(item);
        InvalidateBounds();
    }

    public IEnumerable<T> Query(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        _queryScratch.Clear();
        foreach (var cell in GetCells(viewport))
        {
            if (_grid.TryGetValue(cell, out var set))
            {
                foreach (var item in set)
                {
                    if (!_queryScratch.Add(item)) continue;

                    var itemBounds = item.Bounds;
                    // Zero-size items (e.g. nodes not yet measured by the view) are tested
                    // as points so they appear immediately after being added programmatically.
                    bool visible = itemBounds.IsEmpty
                        ? viewport.Contains(itemBounds.Horizontal, itemBounds.Vertical)
                        : viewport.IntersectsWith(itemBounds);
                    if (visible)
                        yield return item;
                }
            }
        }
    }

    public void Clear()
    {
        // Iterate over a snapshot (Keys copy) because UnregisterItem only
        // unsubscribes events without modifying the dictionary, so this is safe.
        // But use ToArray to guarantee robustness if the caller pattern changes.
        foreach (var item in _trackedItems.Keys)
            UnregisterItem(item);
        _trackedItems.Clear();
        _grid.Clear();
        _queryScratch.Clear();
        _bounds = Viewport.Empty;
        _boundsDirty = false;
    }

    private void RegisterItem(T item, Viewport initialBounds)
    {
        item.PropertyChanged += OnItemPropertyChanged;
        _trackedItems[item] = initialBounds;
    }

    private void UnregisterItem(T item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not T item) return;

        if (e.PropertyName != nameof(ISpatialBoundsProvider.Bounds))
            return;

        if (!_trackedItems.TryGetValue(item, out var oldBounds)) return;

        var newBounds = item.Bounds;
        if (oldBounds.Equals(newBounds)) return;

        // Re-entrancy guard: a zoom cascade fires many items' Bounds changes; mutating the grid
        // Dictionary while it is mid-operation corrupts its internal state (a later set_Item can
        // then throw IndexOutOfRange). Defer the grid update and re-sync once the outer pass settles.
        if (_reindexing)
        {
            _trackedItems[item] = newBounds;
            _rerunPending = true;
            return;
        }

        _reindexing = true;
        try
        {
            // Gracefully handle transitions between empty and non-empty bounds.
            // Items with empty/NaN bounds are tracked but not indexed; when their
            // bounds become real, index them (PropertyChanged is reliable after view layout).
            if (oldBounds.IsEmpty)
            {
                if (!newBounds.IsEmpty)
                    IndexItem(item, newBounds);
            }
            else if (newBounds.IsEmpty)
            {
                DeindexItem(item, oldBounds);
            }
            else
            {
                DeindexItem(item, oldBounds);
                IndexItem(item, newBounds);
            }

            _trackedItems[item] = newBounds;
            InvalidateBounds();
        }
        finally
        {
            _reindexing = false;
        }

        if (_rerunPending)
        {
            _rerunPending = false;
            ResyncGrid();
        }
    }

    /// <summary>Rebuilds the grid from the tracked items' current bounds, picking up any bounds that
    /// changed during a nested pass (deferred by the re-entrancy guard).</summary>
    private void ResyncGrid()
    {
        _reindexing = true;
        try
        {
            _grid.Clear();
            foreach (var pair in _trackedItems.ToArray())
            {
                if (!pair.Value.IsEmpty)
                    IndexItem(pair.Key, pair.Value);
            }
            _queryScratch.Clear();
        }
        finally
        {
            _reindexing = false;
        }
    }

    private void IndexItem(T item, Viewport bounds)
    {
        foreach (var cell in GetCells(bounds))
        {
            if (!_grid.TryGetValue(cell, out var set))
            {
                set = [];
                _grid[cell] = set;
            }
            set.Add(item);
        }
    }

    private void DeindexItem(T item, Viewport bounds)
    {
        foreach (var cell in GetCells(bounds))
        {
            if (_grid.TryGetValue(cell, out var set))
            {
                set.Remove(item);
                if (set.Count == 0) _grid.Remove(cell);
            }
        }
    }

    private CellEnumerable GetCells(Viewport bounds) => new(bounds, _cellSize);

    private readonly struct CellEnumerable
    {
        private readonly Viewport _bounds;
        private readonly double _cellSize;

        internal CellEnumerable(Viewport bounds, double cellSize)
        {
            _bounds = bounds;
            _cellSize = cellSize;
        }

        public CellEnumerator GetEnumerator() => new(_bounds, _cellSize);
    }

    private struct CellEnumerator
    {
        private readonly int _minX;
        private readonly int _maxX;
        private readonly int _minY;
        private readonly int _maxY;
        private int _x;
        private int _y;
        private bool _started;

        internal CellEnumerator(Viewport bounds, double cellSize)
        {
            if (bounds.IsEmpty)
            {
                // Zero-size bounds: just the single cell containing the anchor point.
                _minX = (int)Math.Floor(bounds.Horizontal / cellSize);
                _minY = (int)Math.Floor(bounds.Vertical / cellSize);
                _maxX = _minX + 1;
                _maxY = _minY + 1;
            }
            else
            {
                _minX = (int)Math.Floor(bounds.Horizontal / cellSize);
                _maxX = (int)Math.Ceiling(bounds.Right / cellSize);
                _minY = (int)Math.Floor(bounds.Vertical / cellSize);
                _maxY = (int)Math.Ceiling(bounds.Bottom / cellSize);
            }
            _x = _minX;
            _y = _minY;
            _started = false;
        }

        public CellKey Current => new(_x, _y);

        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                return _x < _maxX && _y < _maxY;
            }

            _x++;
            if (_x >= _maxX)
            {
                _x = _minX;
                _y++;
            }
            return _y < _maxY;
        }
    }
}