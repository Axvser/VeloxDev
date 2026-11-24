using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Manages spatial indexing for nodes via AgentBounds (NodePairBoundsProvider).
/// Links are derived from AgentBounds — querying the spatial grid for intersecting
/// node pairs yields both endpoint nodes and their connecting link.
/// </summary>
public sealed class WorkflowSpatialManager : IDisposable
{
    private readonly ITreeComponent _tree;
    private readonly SpatialGridHashMap<NodeBoundsProvider> _nodeMap;
    private readonly SpatialGridHashMap<NodePairBoundsProvider> _nodePairMap;
    private readonly Dictionary<INodeComponent, NodeBoundsProvider> _nodeProviders = [];
    private readonly Dictionary<ILinkComponent, NodePairBoundsProvider> _nodePairProviders = [];
    private readonly Dictionary<NodePairBoundsProvider, ILinkComponent> _pairToLink = [];
    // Reverse index: for each node, all NodePairBoundsProviders that have it as an endpoint.
    // Used by depth-expanded AgentBounds queries to walk the connection graph.
    private readonly Dictionary<INodeComponent, List<NodePairBoundsProvider>> _nodeToPairs = [];
    // Links whose LinkAdded fired before one of their endpoint nodes was registered. They are
    // retried on every OnNodeAdded; without this they would be silently dropped forever and could
    // never enter VisibleItems — "slot colored but link never visible".
    private readonly HashSet<ILinkComponent> _pendingLinks = [];
    private readonly double _cellSize;
    private bool _disposed;

    // 单趟虚拟化复用的缓冲（见 WorkflowSpatialEx.VirtualizeCore）。树上的重入守卫保证同一时刻只有一趟在用它。
    internal VirtualizationScratch Scratch { get; } = new();

    /// <summary>Gets the minimal viewport that covers all indexed nodes and node pairs.</summary>
    public Viewport GlobalBounds => Viewport.Union(_nodeMap.Bounds, _nodePairMap.Bounds);

    /// <summary>Creates a manager over <paramref name="tree"/> with the given spatial cell size.</summary>
    public WorkflowSpatialManager(ITreeComponent tree, double cellSize)
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

        // 暂存的连线只靠 NodeAdded 补挂 —— 若此后没有新节点，它们就永远进不了网格，而查询是唯一必须说真话的
        // 地方。补挂本身很便宜（正常时集合是空的）。
        RetryPendingLinks();

        // 1. Collect directly visible pairs from spatial grid
        var seenPairs = new HashSet<NodePairBoundsProvider>();
        var seenNodes = new HashSet<INodeComponent>();
        var directPairs = new List<NodePairBoundsProvider>();
        var frontier = new List<INodeComponent>();

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
            var nextFrontier = new List<INodeComponent>();
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
    public IEnumerable<INodeComponent> QueryNodes(Viewport viewport)
    {
        if (viewport.IsEmpty) yield break;

        foreach (var provider in _nodeMap.Query(viewport))
            yield return provider.Node;
    }

    /// <summary>
    /// Returns the links corresponding to the specified AgentBounds (node pairs).
    /// </summary>
    internal IEnumerable<ILinkComponent> ResolveLinksFromPairs(IEnumerable<NodePairBoundsProvider> pairs)
    {
        foreach (var pair in pairs)
        {
            if (_pairToLink.TryGetValue(pair, out var link))
                yield return link;
        }
    }

    // 与 QueryAgentBounds 同序同结果，结果填进复用的 List，省掉 iterator / ToArray / 每趟新建的集合。
    // 返回的就是 Scratch 里的实例，调用方读完之前不要再触发另一趟虚拟化。
    internal List<NodePairBoundsProvider> CollectAgentBounds(Viewport viewport, int expansionDepth = 1)
    {
        var scratch = Scratch;
        var result = scratch.AgentBounds;
        result.Clear();
        if (viewport.IsEmpty) return result;

        RetryPendingLinks();

        scratch.DirectPairs.Clear();
        _nodePairMap.CollectInto(viewport, scratch.DirectPairs);
        scratch.SeenPairs.Clear();
        scratch.SeenNodes.Clear();
        scratch.Frontier.Clear();
        scratch.NextFrontier.Clear();

        // 1. 直接可见的节点对
        foreach (var provider in scratch.DirectPairs)
        {
            if (!scratch.SeenPairs.Add(provider)) continue;
            result.Add(provider);

            if (scratch.SeenNodes.Add(provider.NodeA)) scratch.Frontier.Add(provider.NodeA);
            if (scratch.SeenNodes.Add(provider.NodeB)) scratch.Frontier.Add(provider.NodeB);
        }

        // 2. 按深度沿反向索引扩张（两层 List 互换角色，避免每层再分配）
        for (var depth = 0; depth < expansionDepth && scratch.Frontier.Count > 0; depth++)
        {
            var next = scratch.NextFrontier;
            next.Clear();
            foreach (var node in scratch.Frontier)
            {
                if (!_nodeToPairs.TryGetValue(node, out var connectedPairs)) continue;

                foreach (var pair in connectedPairs)
                {
                    if (!scratch.SeenPairs.Add(pair)) continue;
                    result.Add(pair);

                    var other = ReferenceEquals(pair.NodeA, node) ? pair.NodeB : pair.NodeA;
                    if (scratch.SeenNodes.Add(other)) next.Add(other);
                }
            }

            (scratch.Frontier, scratch.NextFrontier) = (next, scratch.Frontier);
        }

        return result;
    }

    // 与 QueryNodes 同序，填进复用 List。
    internal List<NodeBoundsProvider> CollectNodeProviders(Viewport viewport)
    {
        var result = Scratch.NodeProviders;
        result.Clear();
        if (viewport.IsEmpty) return result;

        _nodeMap.CollectInto(viewport, result);
        return result;
    }

    // 与 ResolveLinksFromPairs 的单次查表同义，省掉 iterator。
    internal bool TryGetLink(NodePairBoundsProvider pair, out ILinkComponent link)
    {
        if (_pairToLink.TryGetValue(pair, out var resolved))
        {
            link = resolved;
            return true;
        }

        link = null!;
        return false;
    }

    private void InsertLink(ILinkComponent link)
    {
        if (link == null || _nodePairProviders.ContainsKey(link)) return;

        // Insert an AgentBounds proxy so the spatial grid can detect that both
        // endpoints should be considered visible when their combined bounds
        // intersect the viewport (e.g. a long link crossing the viewport).
        if (link.Sender?.Parent is INodeComponent nodeA &&
            link.Receiver?.Parent is INodeComponent nodeB &&
            nodeA != nodeB &&
            _nodeProviders.TryGetValue(nodeA, out var providerA) &&
            _nodeProviders.TryGetValue(nodeB, out var providerB))
        {
            _pendingLinks.Remove(link);

            var pairProvider = new NodePairBoundsProvider(nodeA, nodeB, providerA, providerB);
            _nodePairProviders[link] = pairProvider;
            _pairToLink[pairProvider] = link;
            _nodePairMap.Insert(pairProvider);

            // Build reverse index for graph-traversal queries
            AddToNodeIndex(nodeA, pairProvider);
            AddToNodeIndex(nodeB, pairProvider);
        }
        else if (link.Sender?.Parent is INodeComponent &&
                 link.Receiver?.Parent is INodeComponent &&
                 link.Sender.Parent != link.Receiver.Parent)
        {
            // Both endpoints exist and differ, but at least one endpoint node is not registered yet
            // (LinkAdded fired before that node was inserted, e.g. deserialized/undo orderings).
            // Hold it for a retry on the next OnNodeAdded — otherwise it would be silently dropped
            // and never become spatially visible, while the slot state still updates.
            _pendingLinks.Add(link);
        }
    }

    private void RemoveLink(ILinkComponent link)
    {
        if (link == null) return;

        _pendingLinks.Remove(link);

        if (_nodePairProviders.TryGetValue(link, out var pairProvider))
        {
            _nodePairMap.Remove(pairProvider);
            _pairToLink.Remove(pairProvider);
            RemoveFromNodeIndex(pairProvider);
            pairProvider.Dispose();
            _nodePairProviders.Remove(link);
        }
    }

    private void AddToNodeIndex(INodeComponent node, NodePairBoundsProvider pair)
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

    private void InsertNode(INodeComponent node)
    {
        if (node == null || _nodeProviders.ContainsKey(node)) return;

        var provider = new NodeBoundsProvider(node);
        _nodeProviders[node] = provider;
        _nodeMap.Insert(provider);
    }

    private void RemoveNode(INodeComponent node)
    {
        if (node == null || !_nodeProviders.TryGetValue(node, out var provider)) return;

        _nodeMap.Remove(provider);
        provider.Dispose();
        _nodeProviders.Remove(node);
    }

    private void OnNodeAdded(object? sender, INodeComponent node)
    {
        InsertNode(node);
        // A node may have been registered after some of its links were added (LinkAdded → InsertLink
        // parked them in _pendingLinks). Now that it's indexed, give those links another chance so
        // they enter the spatial grid instead of staying invisible forever.
        RetryPendingLinks();
    }

    private void RetryPendingLinks()
    {
        if (_pendingLinks.Count == 0) return;

        foreach (var link in _pendingLinks.ToArray())
        {
            // InsertLink removes the link from _pendingLinks on success and re-queues it if an
            // endpoint is still missing.
            InsertLink(link);
        }
    }

    private void OnNodeRemoved(object? sender, INodeComponent node)
    {
        RemoveNode(node);
    }

    private void OnLinkAdded(object? sender, ILinkComponent link)
    {
        InsertLink(link);
    }

    private void OnLinkRemoved(object? sender, ILinkComponent link)
    {
        RemoveLink(link);
    }

    /// <inheritdoc />
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
        _pendingLinks.Clear();
        _nodePairMap.Clear();
    }
}

// 单棵树的虚拟化缓冲。Clear 会清掉元素引用，所以两趟之间不会把上一趟的节点/连线留在内存里；
// HashSet 经 Clear 后重新插入仍按插入序枚举，可见集顺序与每趟新建集合时逐位一致。
internal sealed class VirtualizationScratch
{
    internal readonly List<NodeBoundsProvider> NodeProviders = [];
    internal readonly List<NodePairBoundsProvider> AgentBounds = [];
    internal readonly List<NodePairBoundsProvider> DirectPairs = [];
    internal readonly HashSet<NodePairBoundsProvider> SeenPairs = [];
    internal readonly HashSet<INodeComponent> SeenNodes = [];
    // 与可见集/期望集原来的比较器保持一致：组件按引用身份去重。
    internal readonly HashSet<INodeComponent> VisibleNodes =
        new(WorkflowReferenceEqualityComparer<INodeComponent>.Instance);
    internal readonly List<IWorkflowComponent> DesiredItems = [];
    internal readonly HashSet<IWorkflowComponent> DesiredSet =
        new(WorkflowReferenceEqualityComparer<IWorkflowComponent>.Instance);
    // 追加缺失项时对齐 ObservableCollection.Contains 的默认比较器。
    internal readonly HashSet<IWorkflowComponent> ExistingItems = [];
    // 扩张层的前沿：两层 List 每层互换角色。
    internal List<INodeComponent> Frontier = [];
    internal List<INodeComponent> NextFrontier = [];
}
