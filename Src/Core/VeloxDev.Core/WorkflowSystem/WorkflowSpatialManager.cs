namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Manages spatial indexing for both nodes and links in a workflow tree.
/// This class provides unified spatial queries that can correctly handle 
/// links spanning across distant nodes.
/// </summary>
public sealed class WorkflowSpatialManager : IDisposable
{
    private readonly IWorkflowTreeViewModel _tree;
    private readonly SpatialGridHashMap<NodeBoundsProvider> _nodeMap;
    private readonly SpatialGridHashMap<LinkBoundsProvider> _linkMap;
    private readonly Dictionary<IWorkflowNodeViewModel, NodeBoundsProvider> _nodeProviders = [];
    private readonly Dictionary<IWorkflowLinkViewModel, LinkBoundsProvider> _linkProviders = [];
    private readonly double _cellSize;
    private bool _disposed;

    public WorkflowSpatialManager(IWorkflowTreeViewModel tree, double cellSize)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _cellSize = Math.Max(1d, cellSize);

        _nodeMap = new SpatialGridHashMap<NodeBoundsProvider>(_cellSize);
        _linkMap = new SpatialGridHashMap<LinkBoundsProvider>(_cellSize);

        Initialize();
    }

    private void Initialize()
    {
        // Index existing nodes
        if (_tree.Nodes != null)
        {
            foreach (var node in _tree.Nodes)
            {
                InsertNode(node);
            }
        }

        // Index existing links
        if (_tree.Links != null)
        {
            foreach (var link in _tree.Links)
            {
                InsertLink(link);
            }
        }

        // Subscribe to tree helper events
        var helper = _tree.GetHelper();
        helper.NodeAdded += OnNodeAdded;
        helper.NodeRemoved += OnNodeRemoved;
        helper.LinkAdded += OnLinkAdded;
        helper.LinkRemoved += OnLinkRemoved;
    }

    /// <summary>
    /// Queries all nodes that intersect with the specified viewport.
    /// </summary>
    public IEnumerable<IWorkflowNodeViewModel> QueryNodes(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        foreach (var provider in _nodeMap.Query(viewport))
        {
            yield return provider.Node;
        }
    }

    /// <summary>
    /// Queries all links that intersect with the specified viewport.
    /// This correctly handles links that span across distant nodes.
    /// </summary>
    public IEnumerable<IWorkflowLinkViewModel> QueryLinks(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        foreach (var provider in _linkMap.Query(viewport))
        {
            yield return provider.Link;
        }
    }

    /// <summary>
    /// Queries all workflow view models (nodes and links) that intersect with the specified viewport.
    /// </summary>
    public IEnumerable<IWorkflowViewModel> QueryAll(Viewport viewport)
    {
        foreach (var node in QueryNodes(viewport))
        {
            yield return node;
        }

        foreach (var link in QueryLinks(viewport))
        {
            yield return link;
        }
    }

    private void InsertLink(IWorkflowLinkViewModel link)
    {
        if (link == null || _linkProviders.ContainsKey(link)) return;

        var provider = new LinkBoundsProvider(link);
        _linkProviders[link] = provider;
        _linkMap.Insert(provider);
    }

    private void RemoveLink(IWorkflowLinkViewModel link)
    {
        if (link == null || !_linkProviders.TryGetValue(link, out var provider)) return;

        _linkMap.Remove(provider);
        provider.Dispose();
        _linkProviders.Remove(link);
    }

    private void InsertNode(IWorkflowNodeViewModel node)
    {
        if (node == null || _nodeProviders.ContainsKey(node)) return;

        var provider = new NodeBoundsProvider(node);
        _nodeProviders[node] = provider;
        _nodeMap.Insert(provider);
    }

    private void RemoveNode(IWorkflowNodeViewModel node)
    {
        if (node == null || !_nodeProviders.TryGetValue(node, out var provider)) return;

        _nodeMap.Remove(provider);
        provider.Dispose();
        _nodeProviders.Remove(node);
    }

    private void OnNodeAdded(object? sender, IWorkflowNodeViewModel node)
    {
        InsertNode(node);
    }

    private void OnNodeRemoved(object? sender, IWorkflowNodeViewModel node)
    {
        RemoveNode(node);
    }

    private void OnLinkAdded(object? sender, IWorkflowLinkViewModel link)
    {
        InsertLink(link);
    }

    private void OnLinkRemoved(object? sender, IWorkflowLinkViewModel link)
    {
        RemoveLink(link);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var helper = _tree.GetHelper();
        helper.NodeAdded -= OnNodeAdded;
        helper.NodeRemoved -= OnNodeRemoved;
        helper.LinkAdded -= OnLinkAdded;
        helper.LinkRemoved -= OnLinkRemoved;

        foreach (var provider in _nodeProviders.Values)
        {
            provider.Dispose();
        }
        _nodeProviders.Clear();
        _nodeMap.Clear();

        foreach (var provider in _linkProviders.Values)
        {
            provider.Dispose();
        }
        _linkProviders.Clear();
        _linkMap.Clear();
    }
}
