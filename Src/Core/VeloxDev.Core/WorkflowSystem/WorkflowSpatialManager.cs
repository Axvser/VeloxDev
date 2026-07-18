namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Manages spatial indexing for nodes via AgentBounds (NodePairBoundsProvider).
/// Links are derived from AgentBounds — querying the spatial grid for intersecting
/// node pairs yields both endpoint nodes and their connecting link.
/// </summary>
public sealed class WorkflowSpatialManager : IDisposable
{
    private readonly IWorkflowTreeViewModel _tree;
    private readonly SpatialGridHashMap<NodeBoundsProvider> _nodeMap;
    private readonly SpatialGridHashMap<NodePairBoundsProvider> _nodePairMap;
    private readonly Dictionary<IWorkflowNodeViewModel, NodeBoundsProvider> _nodeProviders = [];
    private readonly Dictionary<IWorkflowLinkViewModel, NodePairBoundsProvider> _nodePairProviders = [];
    private readonly Dictionary<NodePairBoundsProvider, IWorkflowLinkViewModel> _pairToLink = [];
    // Reverse index: for each node, all NodePairBoundsProviders that have it as an endpoint.
    // Used by depth-expanded AgentBounds queries to walk the connection graph.
    private readonly Dictionary<IWorkflowNodeViewModel, List<NodePairBoundsProvider>> _nodeToPairs = [];
    private readonly double _cellSize;
    private bool _disposed;

    /// <summary>Gets the minimal viewport that covers all indexed nodes and node pairs.</summary>
    public Viewport GlobalBounds => Viewport.Union(_nodeMap.Bounds, _nodePairMap.Bounds);

    public WorkflowSpatialManager(IWorkflowTreeViewModel tree, double cellSize)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _cellSize = Math.Max(1d, cellSize);

        _nodeMap = new SpatialGridHashMap<NodeBoundsProvider>(_cellSize);
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
    /// Queries all AgentBounds (node pair providers) that intersect with the specified viewport,
    /// then expands outward through the connection graph by the specified depth.
    /// </summary>
    /// <param name="viewport">The visible region.</param>
    /// <param name="expansionDepth">
    /// Number of extra hops beyond directly visible pairs. Depth 0 = only pairs intersecting
    /// the viewport. Depth 1 = visible pairs plus pairs connected to their endpoint nodes.
    /// Default is 1, which ensures that when a node is brought in via one link, all its other
    /// connections are also included (preventing link flicker when the node becomes fully visible).
    /// </param>
    internal IEnumerable<NodePairBoundsProvider> QueryAgentBounds(Viewport viewport, int expansionDepth = 1)
    {
        if (viewport.IsEmpty) yield break;

        // 1. Collect directly visible pairs from spatial grid
        var seenPairs = new HashSet<NodePairBoundsProvider>();
        var seenNodes = new HashSet<IWorkflowNodeViewModel>();
        var directPairs = new List<NodePairBoundsProvider>();
        var frontier = new List<IWorkflowNodeViewModel>();

        foreach (var provider in _nodePairMap.Query(viewport))
        {
            if (!seenPairs.Add(provider)) continue;
            directPairs.Add(provider);
            yield return provider;

            if (seenNodes.Add(provider.NodeA)) frontier.Add(provider.NodeA);
            if (seenNodes.Add(provider.NodeB)) frontier.Add(provider.NodeB);
        }

        // 2. Expand by depth: walk connected pairs via reverse index
        for (int d = 0; d < expansionDepth && frontier.Count > 0; d++)
        {
            var nextFrontier = new List<IWorkflowNodeViewModel>();
            foreach (var node in frontier)
            {
                if (!_nodeToPairs.TryGetValue(node, out var connectedPairs)) continue;

                foreach (var pair in connectedPairs)
                {
                    if (!seenPairs.Add(pair)) continue;
                    yield return pair;

                    // Add the opposite endpoint for the next depth level
                    var other = ReferenceEquals(pair.NodeA, node) ? pair.NodeB : pair.NodeA;
                    if (seenNodes.Add(other))
                        nextFrontier.Add(other);
                }
            }
            frontier = nextFrontier;
        }
    }

    /// <summary>
    /// Queries all individually visible nodes that intersect with the specified viewport.
    /// Isolated nodes (not connected by any link) are surfaced here.
    /// </summary>
    public IEnumerable<IWorkflowNodeViewModel> QueryNodes(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        foreach (var provider in _nodeMap.Query(viewport))
            yield return provider.Node;
    }

    /// <summary>
    /// Returns the links corresponding to the specified AgentBounds (node pairs).
    /// </summary>
    internal IEnumerable<IWorkflowLinkViewModel> ResolveLinksFromPairs(IEnumerable<NodePairBoundsProvider> pairs)
    {
        foreach (var pair in pairs)
        {
            if (_pairToLink.TryGetValue(pair, out var link))
                yield return link;
        }
    }

    private void InsertLink(IWorkflowLinkViewModel link)
    {
        if (link == null || _nodePairProviders.ContainsKey(link)) return;

        // Insert an AgentBounds proxy so the spatial grid can detect that both
        // endpoints should be considered visible when their combined bounds
        // intersect the viewport (e.g. a long link crossing the viewport).
        if (link.Sender?.Parent is IWorkflowNodeViewModel nodeA &&
            link.Receiver?.Parent is IWorkflowNodeViewModel nodeB &&
            nodeA != nodeB &&
            _nodeProviders.TryGetValue(nodeA, out var providerA) &&
            _nodeProviders.TryGetValue(nodeB, out var providerB))
        {
            var pairProvider = new NodePairBoundsProvider(nodeA, nodeB, providerA, providerB);
            _nodePairProviders[link] = pairProvider;
            _pairToLink[pairProvider] = link;
            _nodePairMap.Insert(pairProvider);

            // Build reverse index for graph-traversal queries
            AddToNodeIndex(nodeA, pairProvider);
            AddToNodeIndex(nodeB, pairProvider);
        }
    }

    private void RemoveLink(IWorkflowLinkViewModel link)
    {
        if (link == null) return;

        if (_nodePairProviders.TryGetValue(link, out var pairProvider))
        {
            _nodePairMap.Remove(pairProvider);
            _pairToLink.Remove(pairProvider);
            RemoveFromNodeIndex(pairProvider);
            pairProvider.Dispose();
            _nodePairProviders.Remove(link);
        }
    }

    private void AddToNodeIndex(IWorkflowNodeViewModel node, NodePairBoundsProvider pair)
    {
        if (!_nodeToPairs.TryGetValue(node, out var list))
        {
            list = [];
            _nodeToPairs[node] = list;
        }
        list.Add(pair);
    }

    private void RemoveFromNodeIndex(NodePairBoundsProvider pair)
    {
        if (_nodeToPairs.TryGetValue(pair.NodeA, out var listA))
        {
            listA.Remove(pair);
            if (listA.Count == 0) _nodeToPairs.Remove(pair.NodeA);
        }
        if (_nodeToPairs.TryGetValue(pair.NodeB, out var listB))
        {
            listB.Remove(pair);
            if (listB.Count == 0) _nodeToPairs.Remove(pair.NodeB);
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

        foreach (var provider in _nodePairProviders.Values)
        {
            provider.Dispose();
        }
        _nodePairProviders.Clear();
        _pairToLink.Clear();
        _nodeToPairs.Clear();
        _nodePairMap.Clear();
    }
}
