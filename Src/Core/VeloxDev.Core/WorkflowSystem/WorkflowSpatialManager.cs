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
    private readonly SpatialGridHashMap<NodePairBoundsProvider> _nodePairMap;
    private readonly Dictionary<IWorkflowNodeViewModel, NodeBoundsProvider> _nodeProviders = [];
    private readonly Dictionary<IWorkflowLinkViewModel, LinkBoundsProvider> _linkProviders = [];
    private readonly Dictionary<IWorkflowLinkViewModel, NodePairBoundsProvider> _nodePairProviders = [];
    private readonly double _cellSize;
    private bool _disposed;

    /// <summary>Gets the minimal viewport that covers all indexed nodes, links, and node pairs.</summary>
    public Viewport GlobalBounds => Viewport.Union(Viewport.Union(_nodeMap.Bounds, _linkMap.Bounds), _nodePairMap.Bounds);

    public WorkflowSpatialManager(IWorkflowTreeViewModel tree, double cellSize)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _cellSize = Math.Max(1d, cellSize);

        _nodeMap = new SpatialGridHashMap<NodeBoundsProvider>(_cellSize);
        _linkMap = new SpatialGridHashMap<LinkBoundsProvider>(_cellSize);
        _nodePairMap = new SpatialGridHashMap<NodePairBoundsProvider>(_cellSize);

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
    /// Also includes nodes brought into view by visible link connections,
    /// i.e. nodes whose combined bounds with a connected node intersect
    /// the viewport even if neither node individually does.
    /// </summary>
    public IEnumerable<IWorkflowNodeViewModel> QueryNodes(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        var seen = new HashSet<IWorkflowNodeViewModel>();

        foreach (var provider in _nodeMap.Query(viewport))
        {
            if (seen.Add(provider.Node))
                yield return provider.Node;
        }

        foreach (var pairProvider in _nodePairMap.Query(viewport))
        {
            bool aSeen = seen.Contains(pairProvider.NodeA);
            bool bSeen = seen.Contains(pairProvider.NodeB);

            // Both nodes are already individually visible — nothing to add.
            if (aSeen && bSeen) continue;

            // At least one node is individually visible — the other endpoint
            // will be pulled in by Virtualize step 4 (link derivation) via
            // the 1-hop expandedNodes set.  No need to surface it here.
            if (aSeen || bSeen) continue;

            // Neither node is individually visible, yet the NodePair union
            // bounds intersect the viewport — a link crosses the viewport
            // with both endpoints outside.  Surface both so the link can render.
            seen.Add(pairProvider.NodeA);
            yield return pairProvider.NodeA;
            seen.Add(pairProvider.NodeB);
            yield return pairProvider.NodeB;
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

        // Insert a node-pair proxy so the spatial grid can detect that both
        // endpoints should be considered visible even when neither node's own
        // bounds intersect the viewport (e.g. a long link crossing the viewport
        // with both endpoints outside it).
        if (link.Sender?.Parent is IWorkflowNodeViewModel nodeA &&
            link.Receiver?.Parent is IWorkflowNodeViewModel nodeB &&
            nodeA != nodeB &&
            _nodeProviders.TryGetValue(nodeA, out var providerA) &&
            _nodeProviders.TryGetValue(nodeB, out var providerB))
        {
            var pairProvider = new NodePairBoundsProvider(nodeA, nodeB, providerA, providerB);
            _nodePairProviders[link] = pairProvider;
            _nodePairMap.Insert(pairProvider);
        }
    }

    private void RemoveLink(IWorkflowLinkViewModel link)
    {
        if (link == null || !_linkProviders.TryGetValue(link, out var provider)) return;

        _linkMap.Remove(provider);
        provider.Dispose();
        _linkProviders.Remove(link);

        if (_nodePairProviders.TryGetValue(link, out var pairProvider))
        {
            _nodePairMap.Remove(pairProvider);
            pairProvider.Dispose();
            _nodePairProviders.Remove(link);
        }
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

        foreach (var provider in _nodePairProviders.Values)
        {
            provider.Dispose();
        }
        _nodePairProviders.Clear();
        _nodePairMap.Clear();
    }
}
