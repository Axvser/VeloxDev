using System;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

internal readonly struct CellKey(int x, int y) : IEquatable<CellKey>
{
    public readonly int X = x;
    public readonly int Y = y;

    public bool Equals(CellKey other) => X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is CellKey k && Equals(k);

    public override int GetHashCode() => HashCode.Combine(X, Y);
}

public class SpatialHashMap(double cellSize = 200d)
{
    private readonly Dictionary<CellKey, List<IWorkflowNodeViewModel>> _grid = [];
    private readonly double _cellSize = Math.Max(1d, cellSize);

    // 插入节点：直接使用 Anchor + Size
    public void Insert(IWorkflowNodeViewModel node)
    {
        if (node == null) return;

        var cells = GetCells(
            node.Anchor.Left,
            node.Anchor.Top,
            node.Size.Width,
            node.Size.Height);

        foreach (var cell in cells)
        {
            if (!_grid.TryGetValue(cell, out var list))
                _grid[cell] = list = [];
            list.Add(node);
        }
    }

    // 移除节点：同样基于 Anchor + Size
    public void Remove(IWorkflowNodeViewModel node)
    {
        if (node == null) return;

        var cells = GetCells(
            node.Anchor.Left,
            node.Anchor.Top,
            node.Size.Width,
            node.Size.Height);

        foreach (var cell in cells)
        {
            if (_grid.TryGetValue(cell, out var list))
                list.Remove(node);
        }
    }

    // 查询：传入 viewport 的 left, top, width, height
    public IEnumerable<IWorkflowNodeViewModel> Query(
        double viewportLeft,
        double viewportTop,
        double viewportWidth,
        double viewportHeight)
    {
        var cells = GetCells(viewportLeft, viewportTop, viewportWidth, viewportHeight);
        var seen = new HashSet<IWorkflowNodeViewModel>();

        foreach (var cell in cells)
        {
            if (_grid.TryGetValue(cell, out var list))
            {
                foreach (var node in list)
                {
                    if (!seen.Add(node)) continue;

                    // 手动相交检测（无 Rect 创建）
                    double nodeRight = node.Anchor.Left + node.Size.Width;
                    double nodeBottom = node.Anchor.Top + node.Size.Height;
                    double viewRight = viewportLeft + viewportWidth;
                    double viewBottom = viewportTop + viewportHeight;

                    if (node.Anchor.Left < viewRight &&
                        nodeRight > viewportLeft &&
                        node.Anchor.Top < viewBottom &&
                        nodeBottom > viewportTop)
                    {
                        yield return node;
                    }
                }
            }
        }
    }

    // 清空索引
    public void Clear() => _grid.Clear();

    // 获取覆盖的格子（返回 struct，无分配）
    private IEnumerable<CellKey> GetCells(double left, double top, double width, double height)
    {
        if (width <= 0 || height <= 0)
            yield break;

        int minX = (int)Math.Floor(left / _cellSize);
        int maxX = (int)Math.Ceiling((left + width) / _cellSize);
        int minY = (int)Math.Floor(top / _cellSize);
        int maxY = (int)Math.Ceiling((top + height) / _cellSize);

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                yield return new CellKey(x, y);
    }
}