using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace VeloxDev.Core.WorkflowSystem.StandardEx;

#pragma warning disable

public static class WorkflowSpatialEx
{
    private static readonly ConditionalWeakTable<object, IWorkflowSpatialMap> SpatialMaps = new();
    private static readonly ConditionalWeakTable<object, object> Observables = new();

    /// <summary>
    /// Enables spatial virtualization for the specified workflow tree view model by creating or retrieving a spatial
    /// hash map with the given cell size.
    /// </summary>
    /// 
    /// <remarks>
    /// If a spatial hash map has already been created for the specified workflow tree view model,
    /// the existing map is returned instead of creating a new one.
    /// </remarks>
    /// 
    /// <param name="tree">
    /// The workflow tree view model for which to enable virtualization. This parameter cannot be null.
    /// </param>
    /// 
    /// <param name="cellSize">
    /// The size of each cell in the spatial hash map, which determines the granularity of virtualization. Must be a
    /// positive value.
    /// </param>
    /// 
    /// <param name="observable">
    /// The observable collection that will be used to track the visible nodes and links.
    /// </param>
    public static int EnableMap<T>(this IWorkflowTreeViewModel tree, double cellSize, T observable)
        where T : Collection<IWorkflowViewModel>, INotifyCollectionChanged
    {
        if (cellSize <= 0)
        {
            return -1;
        }
        if (SpatialMaps.TryGetValue(tree, out _) && Observables.TryGetValue(tree, out _))
        {
            return 0;
        }
        var newMap = new SpatialGridHashMap(cellSize);
        SpatialMaps.Add(tree, newMap);
        Observables.Add(tree, observable);
        tree.GetHelper().NodeAdded += OnNodeAdded;
        tree.GetHelper().NodeRemoved += OnNodeRemoved;
        tree.GetHelper().LinkAdded += OnLinkAdded;
        tree.GetHelper().LinkRemoved += OnLinkRemoved;
        observable.Clear();
        observable.Add(tree.VirtualLink);
        if (tree?.Nodes != null)
        {
            foreach (var node in tree.Nodes)
            {
                newMap.Insert(node);
            }
        }
        return 1;
    }

    /// <summary>
    /// Updates the specified observable collection to reflect the nodes and links visible within the given viewport of
    /// the workflow tree, adding or removing items as necessary to match the current visible state.
    /// </summary>
    /// 
    /// <remarks>
    /// This method is intended for use in scenarios where large workflow trees require efficient UI
    /// virtualization. It ensures that only the nodes and links visible in the current viewport are present in the
    /// observable collection, optimizing performance and memory usage. The observable collection is updated in-place,
    /// and callers should ensure that it is properly bound to any UI components that depend on its contents.
    /// </remarks>
    /// 
    /// <param name="tree">
    /// The workflow tree view model that provides the spatial context and link mapping for virtualization.
    /// </param>
    /// 
    /// <param name="viewport">
    /// The viewport defining the visible area of the workflow tree. Determines which nodes and links should be present
    /// in the observable collection.
    /// </param>
    /// 
    /// <exception cref="ArgumentNullException">
    /// Thrown if the spatial map has not been enabled for the workflow tree.
    /// Use <see cref="EnableMap{T}(IWorkflowTreeViewModel, double, T)"/> to enable spatial indexing first.
    /// </exception>
    /// 
    /// <exception cref="ArgumentException">
    /// Thrown if the viewport has invalid dimensions (e.g., zero or negative width/height).
    /// </exception>
    public static void Virtualize(this IWorkflowTreeViewModel tree, Viewport viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        if (!SpatialMaps.TryGetValue(tree, out var map) || !Observables.TryGetValue(tree, out var collection) || collection is not Collection<IWorkflowViewModel> observable)
            throw new ArgumentNullException("The workflow must first successfully enable the spatial map before it can be virtualized.");

        // 网格哈希查询
        HashSet<IWorkflowNodeViewModel> newVisibleNotes = [.. map.Query(viewport)];
        // 原始集合拷贝
        HashSet<IWorkflowViewModel> oldVisibleItems = [.. observable.OfType<IWorkflowViewModel>()];

        // 待移除节点
        var toRemove = oldVisibleItems.OfType<IWorkflowNodeViewModel>().Where(item => !newVisibleNotes.Contains(item)).ToList();
        // 待添加节点
        var toAdd = newVisibleNotes.Where(item => !oldVisibleItems.Contains(item)).ToList();

        // 先移除
        foreach (IWorkflowNodeViewModel node in toRemove)
        {
            observable.Remove(node);
            foreach (var slot in node.Slots)
            {
                // 若发起者与接受者都不可见，则移除连接
                foreach (var target in slot.Targets)
                {
                    if (target.Parent is not null &&
                        slot.Parent is not null &&
                        !newVisibleNotes.Contains(slot.Parent) &&
                        !newVisibleNotes.Contains(target.Parent) &&
                        tree.LinksMap.TryGetValue(target, out var targets) &&
                        targets.TryGetValue(slot, out var link))
                    {
                        observable.Remove(link);
                    }
                }
                foreach (var source in slot.Sources)
                {
                    if (source.Parent is not null &&
                        slot.Parent is not null &&
                        !newVisibleNotes.Contains(source.Parent) &&
                        !newVisibleNotes.Contains(slot.Parent) &&
                        tree.LinksMap.TryGetValue(source, out var targets) &&
                        targets.TryGetValue(slot, out var link))
                    {
                        observable.Remove(link);
                    }
                }
            }
        }

        // 再添加
        foreach (IWorkflowNodeViewModel node in toAdd)
        {
            observable.Add(node);
            foreach (var slot in node.Slots)
            {
                // 若发起者或接受者可见，则添加连接
                foreach (var target in slot.Targets)
                {
                    if (target.Parent is not null &&
                        slot.Parent is not null &&
                        (newVisibleNotes.Contains(target.Parent) || newVisibleNotes.Contains(slot.Parent)) &&
                        tree.LinksMap.TryGetValue(slot, out var targets) &&
                        targets.TryGetValue(target, out var link))
                    {
                        observable.Add(link);
                    }
                }
                foreach (var source in slot.Sources)
                {
                    if (source.Parent is not null &&
                        slot.Parent is not null &&
                        (newVisibleNotes.Contains(slot.Parent) || newVisibleNotes.Contains(source.Parent)) &&
                        tree.LinksMap.TryGetValue(source, out var targets) &&
                        targets.TryGetValue(slot, out var link))
                    {
                        observable.Add(link);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes the specified workflow tree from both spatial maps and observables.
    /// </summary>
    /// 
    /// <remarks>
    /// Use this method to efficiently clear a workflow tree from both spatial maps and observables.
    /// The return value can be used to determine which removals succeeded.
    /// </remarks>
    /// 
    /// <param name="tree">
    /// The workflow tree view model to be removed from spatial maps and observables. Cannot be null.
    /// </param>
    public static int ClearMap(this IWorkflowTreeViewModel tree)
    {
        tree.GetHelper().NodeAdded -= OnNodeAdded;
        tree.GetHelper().NodeRemoved -= OnNodeRemoved;
        tree.GetHelper().LinkAdded -= OnLinkAdded;
        tree.GetHelper().LinkRemoved -= OnLinkRemoved;
        if (SpatialMaps.TryGetValue(tree, out var _spatialHashMap))
            _spatialHashMap.Clear();
        if (Observables.TryGetValue(tree, out var _observable) && _observable is Collection<IWorkflowViewModel> observable)
            observable.Remove(tree.VirtualLink);
        var r1 = SpatialMaps.Remove(tree) ? 1 : 2;
        var r2 = Observables.Remove(tree) ? 4 : 8;
        return r1 | r2;
    }

    /// <summary>
    /// Selects and returns all workflow nodes that intersect with the specified viewport.
    /// This method leverages the spatial hash map for efficient spatial queries, allowing
    /// for optimized retrieval of nodes within a given rectangular area.
    /// </summary>
    /// 
    /// <param name="viewport">
    /// The rectangular region to query for intersecting nodes.
    /// Defined by its left (X), top (Y), width, and height coordinates.
    /// </param>
    /// 
    /// <returns>
    /// An enumerable collection of <see cref="IWorkflowNodeViewModel"/> instances
    /// that intersect with the specified viewport.
    /// </returns>
    /// 
    /// <exception cref="ArgumentNullException">
    /// Thrown if the spatial map has not been enabled for the workflow tree.
    /// Use <see cref="EnableMap{T}(IWorkflowTreeViewModel, double, T)"/> to enable spatial indexing first.
    /// </exception>
    /// 
    /// <exception cref="ArgumentException">
    /// Thrown if the viewport has invalid dimensions (e.g., zero or negative width/height).
    /// </exception>
    public static IEnumerable<IWorkflowNodeViewModel> QueryNodes(this IWorkflowTreeViewModel tree, Viewport viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return [];

        if (!SpatialMaps.TryGetValue(tree, out var map))
            throw new ArgumentNullException(nameof(tree),
                "The workflow must first successfully enable the spatial map before it can be selected.");

        return map.Query(viewport);
    }

    private static void OnNodeAdded(object? sender, IWorkflowNodeViewModel node)
    {
        if (SpatialMaps.TryGetValue(sender, out var _spatialHashMap))
            _spatialHashMap.Insert(node);
    }

    private static void OnNodeRemoved(object? sender, IWorkflowNodeViewModel node)
    {
        if (SpatialMaps.TryGetValue(sender, out var _spatialHashMap))
            _spatialHashMap.Remove(node);
    }

    private static void OnLinkAdded(object? sender, IWorkflowLinkViewModel node)
    {
        if (Observables.TryGetValue(sender, out var collection) && collection is Collection<IWorkflowViewModel> observable)
            observable.Add(node);
    }

    private static void OnLinkRemoved(object? sender, IWorkflowLinkViewModel node)
    {
        if (Observables.TryGetValue(sender, out var collection) && collection is Collection<IWorkflowViewModel> observable)
            observable.Remove(node);
    }
}
