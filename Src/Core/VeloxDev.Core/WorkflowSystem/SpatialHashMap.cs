using VeloxDev.Core.Interfaces.WorkflowSystem;
using System.ComponentModel;

namespace VeloxDev.Core.WorkflowSystem;

public class SpatialHashMap(double cellSize)
{
    private readonly Dictionary<CellKey, HashSet<IWorkflowNodeViewModel>> _grid = [];
    private readonly Dictionary<IWorkflowNodeViewModel, (Viewport viewport, INotifyPropertyChanged notifier)> _trackedNodes = [];
    private readonly double _cellSize = Math.Max(1d, cellSize);

    public void Insert(IWorkflowNodeViewModel node)
    {
        if (node == null) return;

        if (_trackedNodes.ContainsKey(node)) return;

        var viewport = GetCurrentViewport(node);
        RegisterNode(node, viewport);
        IndexNode(node, viewport);
    }

    public void Remove(IWorkflowNodeViewModel node)
    {
        if (node == null || !_trackedNodes.TryGetValue(node, out var entry)) return;

        UnregisterNode(node);
        DeindexNode(node, entry.viewport);
        _trackedNodes.Remove(node);
    }

    public IEnumerable<IWorkflowNodeViewModel> Query(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        var seen = new HashSet<IWorkflowNodeViewModel>();
        foreach (var cell in GetCells(viewport))
        {
            if (_grid.TryGetValue(cell, out var set))
            {
                foreach (var node in set)
                {
                    if (!seen.Add(node)) continue;

                    var nodeViewport = GetCurrentViewport(node);
                    if (viewport.IntersectsWith(nodeViewport))
                        yield return node;
                }
            }
        }
    }

    public void Clear()
    {
        foreach (var node in _trackedNodes.Keys)
            UnregisterNode(node);
        _trackedNodes.Clear();
        _grid.Clear();
    }

    private static Viewport GetCurrentViewport(IWorkflowNodeViewModel node)
        => new(node.Anchor.Left, node.Anchor.Top, node.Size.Width, node.Size.Height);

    private void RegisterNode(IWorkflowNodeViewModel node, Viewport initialViewport)
    {
        var notifier = (INotifyPropertyChanged)node;
        notifier.PropertyChanged += OnNodePropertyChanged;
        _trackedNodes[node] = (initialViewport, notifier);
    }

    private void UnregisterNode(IWorkflowNodeViewModel node)
    {
        if (_trackedNodes.TryGetValue(node, out var entry))
        {
            entry.notifier.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not IWorkflowNodeViewModel node) return;

        if (e.PropertyName != nameof(IWorkflowNodeViewModel.Anchor) &&
            e.PropertyName != nameof(IWorkflowNodeViewModel.Size))
            return;

        if (!_trackedNodes.TryGetValue(node, out var oldEntry)) return;

        var newViewport = GetCurrentViewport(node);
        if (oldEntry.viewport.Equals(newViewport)) return;

        DeindexNode(node, oldEntry.viewport);
        IndexNode(node, newViewport);
        _trackedNodes[node] = (newViewport, oldEntry.notifier);
    }

    private void IndexNode(IWorkflowNodeViewModel node, Viewport viewport)
    {
        foreach (var cell in GetCells(viewport))
        {
            if (!_grid.TryGetValue(cell, out var set))
            {
                set = [];
                _grid[cell] = set;
            }
            set.Add(node);
        }
    }

    private void DeindexNode(IWorkflowNodeViewModel node, Viewport viewport)
    {
        foreach (var cell in GetCells(viewport))
        {
            if (_grid.TryGetValue(cell, out var set))
            {
                set.Remove(node);
                if (set.Count == 0) _grid.Remove(cell);
            }
        }
    }

    private IEnumerable<CellKey> GetCells(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        int minX = (int)Math.Floor(viewport.Left / _cellSize);
        int maxX = (int)Math.Ceiling(viewport.Right / _cellSize);
        int minY = (int)Math.Floor(viewport.Top / _cellSize);
        int maxY = (int)Math.Ceiling(viewport.Bottom / _cellSize);

        for (int x = minX; x < maxX; x++)
            for (int y = minY; y < maxY; y++)
                yield return new CellKey(x, y);
    }
}