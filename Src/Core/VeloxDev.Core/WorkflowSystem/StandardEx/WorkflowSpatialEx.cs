using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem.StandardEx;

#pragma warning disable

public static class WorkflowSpatialEx
{
    private static readonly ConditionalWeakTable<object, WorkflowSpatialManager> SpatialManagers = new();
    private static readonly ConditionalWeakTable<object, object> Observables = new();
    // Re-entrancy guard: keyed by the tree, tracks whether Virtualize is currently in progress.
    // If OnViewportChanged triggers a nested Virtualize call (e.g. because updating VisibleItems
    // fires CollectionChanged → event handler → Viewport changes again), we bail early — the
    // outer call already computes the correct final state.
    private static readonly ConcurrentDictionary<object, byte> Virtualizing = new();

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
        if (SpatialManagers.TryGetValue(tree, out _) && Observables.TryGetValue(tree, out _))
        {
            return 0;
        }

        var manager = new WorkflowSpatialManager(tree, cellSize);
        SpatialManagers.Add(tree, manager);
        Observables.Add(tree, observable);

        observable.Clear();
        observable.Add(tree.VirtualLink);

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

        // Re-entrancy guard: suppress nested Virtualize calls that arise when
        // updating VisibleItems fires CollectionChanged → event handler → Viewport change
        // → another Virtualize.  The outermost call already reaches the correct final state.
        if (!Virtualizing.TryAdd(tree, 0))
            return;
        try
        {
            VirtualizeCore(tree, viewport);
        }
        finally
        {
            Virtualizing.TryRemove(tree, out _);
        }
    }

    private static void VirtualizeCore(IWorkflowTreeViewModel tree, Viewport viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        if (!SpatialManagers.TryGetValue(tree, out var manager) ||
            !Observables.TryGetValue(tree, out var collection) ||
            collection is not Collection<IWorkflowViewModel> observable)
        {
            throw new ArgumentNullException(
                "The workflow must first successfully enable the spatial map before it can be virtualized.");
        }

        // 1. Query AgentBounds
        //    E.g. A visible → A↔B brought in → B↔C, B↔D, B↔E also included
        //    so that when B becomes fully visible, all its connections are ready
        //    without flicker.
        var agentBounds = manager.QueryAgentBounds(viewport, expansionDepth: 1).ToArray();

        // 2. Collect unique nodes from both ends of each AgentBounds
        var visibleNodes = new HashSet<IWorkflowNodeViewModel>(
            WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);
        foreach (var pair in agentBounds)
        {
            visibleNodes.Add(pair.NodeA);
            visibleNodes.Add(pair.NodeB);
        }

        // 3. Also collect individually visible nodes (isolated nodes with no links)
        foreach (var node in manager.QueryNodes(viewport))
            visibleNodes.Add(node);

        // 4. Build desired items: VirtualLink + all nodes + all links, in one pass
        List<IWorkflowViewModel> desiredItems = [tree.VirtualLink];
        var desiredSet = new HashSet<IWorkflowViewModel>(
            WorkflowReferenceEqualityComparer<IWorkflowViewModel>.Instance)
        {
            tree.VirtualLink
        };

        foreach (var node in visibleNodes)
            AddDesiredItem(desiredItems, desiredSet, node);

        foreach (var link in manager.ResolveLinksFromPairs(agentBounds))
            AddDesiredItem(desiredItems, desiredSet, link);

        // 5. Remove stale items from observable
        foreach (var item in observable.OfType<IWorkflowViewModel>().ToArray())
        {
            if (!desiredSet.Contains(item))
                RemoveAll(observable, item);
        }

        // 6. Add desired items (nodes + links, no deferral)
        foreach (var item in desiredItems)
            AddIfMissing(observable, item);
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
        var r1 = 2;
        var r2 = 8;

        if (SpatialManagers.TryGetValue(tree, out var manager))
        {
            manager.Dispose();
            r1 = SpatialManagers.Remove(tree) ? 1 : 2;
        }

        if (Observables.TryGetValue(tree, out var _observable) && _observable is Collection<IWorkflowViewModel> observable)
        {
            observable.Remove(tree.VirtualLink);
            r2 = Observables.Remove(tree) ? 4 : 8;
        }

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

        if (!SpatialManagers.TryGetValue(tree, out var manager))
            throw new ArgumentNullException(nameof(tree),
                "The workflow must first successfully enable the spatial map before it can be selected.");

        return manager.QueryNodes(viewport);
    }

    private static void AddIfMissing(Collection<IWorkflowViewModel> observable, IWorkflowViewModel item)
    {
        if (!observable.Contains(item))
        {
            observable.Add(item);
        }
    }

    private static void AddDesiredItem(List<IWorkflowViewModel> desiredItems, HashSet<IWorkflowViewModel> desiredSet, IWorkflowViewModel item)
    {
        if (desiredSet.Add(item))
        {
            desiredItems.Add(item);
        }
    }

    private static void RemoveAll(Collection<IWorkflowViewModel> observable, IWorkflowViewModel item)
    {
        while (observable.Contains(item))
        {
            observable.Remove(item);
        }
    }

    private sealed class WorkflowReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly WorkflowReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
