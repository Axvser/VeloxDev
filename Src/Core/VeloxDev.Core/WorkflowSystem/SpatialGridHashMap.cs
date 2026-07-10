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
    private Viewport _bounds;
    private bool _boundsDirty;

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
        IndexItem(item, b);
        InvalidateBounds();
    }

    public void Remove(T item)
    {
        if (item == null || !_trackedItems.TryGetValue(item, out var b)) return;

        UnregisterItem(item);
        DeindexItem(item, b);
        _trackedItems.Remove(item);
        InvalidateBounds();
    }

    public IEnumerable<T> Query(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        var seen = new HashSet<T>();
        foreach (var cell in GetCells(viewport))
        {
            if (_grid.TryGetValue(cell, out var set))
            {
                foreach (var item in set)
                {
                    if (!seen.Add(item)) continue;

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
        foreach (var item in _trackedItems.Keys.ToArray())
            UnregisterItem(item);
        _trackedItems.Clear();
        _grid.Clear();
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

        DeindexItem(item, oldBounds);
        IndexItem(item, newBounds);
        _trackedItems[item] = newBounds;
        InvalidateBounds();
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

    private IEnumerable<CellKey> GetCells(Viewport bounds)
    {
        // Zero-size bounds (e.g. a node that has not yet been measured by the view layer)
        // must still be indexed so that Virtualize can include them in VisibleItems and
        // allow the view to render and measure the node.  Index the single cell that
        // contains the node's anchor point.
        if (bounds.IsEmpty)
        {
            yield return new CellKey(
                (int)Math.Floor(bounds.Horizontal / _cellSize),
                (int)Math.Floor(bounds.Vertical / _cellSize));
            yield break;
        }

        int minX = (int)Math.Floor(bounds.Horizontal / _cellSize);
        int maxX = (int)Math.Ceiling(bounds.Right / _cellSize);
        int minY = (int)Math.Floor(bounds.Vertical / _cellSize);
        int maxY = (int)Math.Ceiling(bounds.Bottom / _cellSize);

        for (int x = minX; x < maxX; x++)
            for (int y = minY; y < maxY; y++)
                yield return new CellKey(x, y);
    }
}