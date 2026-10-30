namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A tile bucket over the snapshot rectangles, keyed by <see cref="CellKey"/>.
/// </summary>
/// <remarks>
/// This is deliberately smaller than <see cref="SpatialGridHashMap{T}"/>: layout objects are immutable
/// values, not <see cref="ISpatialBoundsProvider"/> implementations, and a build-then-query index is enough
/// for one sweep. The caller rebuilds it when positions change.
/// </remarks>
internal sealed class LayoutTileIndex
{
    private readonly double _tileSize;
    private readonly double _originX;
    private readonly double _originY;
    private readonly Dictionary<CellKey, List<int>> _cells = [];

    public LayoutTileIndex(double tileSize, double originX, double originY)
    {
        _tileSize = tileSize > 0 ? tileSize : 1d;
        _originX = originX;
        _originY = originY;
    }

    /// <summary>Returns the cell that contains the point.</summary>
    public CellKey CellOf(double x, double y)
        => new(
            (int)Math.Floor((x - _originX) / _tileSize),
            (int)Math.Floor((y - _originY) / _tileSize));

    /// <summary>Registers <paramref name="index"/> in every cell its rectangle touches.</summary>
    public void Add(int index, WorkflowBounds bounds)
    {
        foreach (var cell in CellsOf(bounds, 0))
        {
            if (!_cells.TryGetValue(cell, out var bucket))
            {
                bucket = [];
                _cells[cell] = bucket;
            }
            bucket.Add(index);
        }
    }

    /// <summary>
    /// Returns the indices registered in the cells covering <paramref name="bounds"/> inflated by
    /// <paramref name="halo"/> tiles, each at most once.
    /// </summary>
    public IEnumerable<int> QueryHalo(WorkflowBounds bounds, int halo)
    {
        var seen = new HashSet<int>();
        foreach (var cell in CellsOf(bounds, halo < 0 ? 0 : halo))
        {
            if (!_cells.TryGetValue(cell, out var bucket)) continue;
            foreach (var index in bucket)
            {
                if (seen.Add(index)) yield return index;
            }
        }
    }

    private IEnumerable<CellKey> CellsOf(WorkflowBounds bounds, int halo)
    {
        var pad = halo * _tileSize;
        var left = bounds.Left - pad;
        var top = bounds.Top - pad;
        var right = bounds.Right + pad;
        var bottom = bounds.Bottom + pad;

        var minX = (int)Math.Floor((left - _originX) / _tileSize);
        var minY = (int)Math.Floor((top - _originY) / _tileSize);
        var maxX = Math.Max(minX + 1, (int)Math.Ceiling((right - _originX) / _tileSize));
        var maxY = Math.Max(minY + 1, (int)Math.Ceiling((bottom - _originY) / _tileSize));

        for (var x = minX; x < maxX; x++)
        {
            for (var y = minY; y < maxY; y++)
            {
                yield return new CellKey(x, y);
            }
        }
    }
}
