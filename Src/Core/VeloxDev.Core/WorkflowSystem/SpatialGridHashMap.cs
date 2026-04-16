using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A generic spatial hash map that supports any type implementing <see cref="ISpatialBoundsProvider"/>.
/// Automatically tracks bounds changes and updates the spatial index accordingly.
/// </summary>
/// <typeparam name="T">The type of elements to store, must implement <see cref="ISpatialBoundsProvider"/>.</typeparam>
public class SpatialGridHashMap<T>(double cellSize) : ISpatialMap<T>
    where T : class, ISpatialBoundsProvider
{
    private readonly Dictionary<CellKey, HashSet<T>> _grid = [];
    private readonly Dictionary<T, Viewport> _trackedItems = [];
    private readonly double _cellSize = Math.Max(1d, cellSize);

    public void Insert(T item)
    {
        if (item == null) return;

        if (_trackedItems.ContainsKey(item)) return;

        var bounds = item.Bounds;
        RegisterItem(item, bounds);
        IndexItem(item, bounds);
    }

    public void Remove(T item)
    {
        if (item == null || !_trackedItems.TryGetValue(item, out var bounds)) return;

        UnregisterItem(item);
        DeindexItem(item, bounds);
        _trackedItems.Remove(item);
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
                    if (viewport.IntersectsWith(itemBounds))
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
        if (bounds.IsEmpty) yield break;

        int minX = (int)Math.Floor(bounds.Horizontal / _cellSize);
        int maxX = (int)Math.Ceiling(bounds.Right / _cellSize);
        int minY = (int)Math.Floor(bounds.Vertical / _cellSize);
        int maxY = (int)Math.Ceiling(bounds.Bottom / _cellSize);

        for (int x = minX; x < maxX; x++)
            for (int y = minY; y < maxY; y++)
                yield return new CellKey(x, y);
    }
}