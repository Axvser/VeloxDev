using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A spatial bounds provider that tracks the combined bounds of two nodes connected by a link.
/// This enables the spatial grid to detect when a link's endpoints become visible even when
/// neither node individually intersects the viewport, by covering the union of both node bounds.
/// 
/// The provider independently subscribes to bounds changes on both endpoint nodes and fires
/// <see cref="PropertyChanged"/> when the combined bounds change, allowing the spatial grid
/// to automatically re-index the pair at the correct grid cells.
/// </summary>
internal sealed class NodePairBoundsProvider : ISpatialBoundsProvider, IDisposable
{
    private static readonly PropertyChangedEventArgs BoundsChangedEventArgs = new(nameof(Bounds));

    private readonly IWorkflowNodeViewModel _nodeA;
    private readonly IWorkflowNodeViewModel _nodeB;
    private readonly ISpatialBoundsProvider _providerA;
    private readonly ISpatialBoundsProvider _providerB;
    private Viewport _cachedBounds;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the first endpoint node.</summary>
    internal IWorkflowNodeViewModel NodeA => _nodeA;

    /// <summary>Gets the second endpoint node.</summary>
    internal IWorkflowNodeViewModel NodeB => _nodeB;

    internal NodePairBoundsProvider(
        IWorkflowNodeViewModel nodeA, IWorkflowNodeViewModel nodeB,
        ISpatialBoundsProvider providerA, ISpatialBoundsProvider providerB)
    {
        _nodeA = nodeA ?? throw new ArgumentNullException(nameof(nodeA));
        _nodeB = nodeB ?? throw new ArgumentNullException(nameof(nodeB));
        _providerA = providerA ?? throw new ArgumentNullException(nameof(providerA));
        _providerB = providerB ?? throw new ArgumentNullException(nameof(providerB));
        _cachedBounds = CalculateBounds();

        _providerA.PropertyChanged += OnNodeBoundsChanged;
        _providerB.PropertyChanged += OnNodeBoundsChanged;
    }

    public Viewport Bounds => _cachedBounds;

    private void OnNodeBoundsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Bounds)) return;
        UpdateBounds();
    }

    private void UpdateBounds()
    {
        var newBounds = CalculateBounds();
        if (!_cachedBounds.Equals(newBounds))
        {
            _cachedBounds = newBounds;
            PropertyChanged?.Invoke(this, BoundsChangedEventArgs);
        }
    }

    private Viewport CalculateBounds()
    {
        var bA = _providerA.Bounds;
        var bB = _providerB.Bounds;

        // If either endpoint hasn't been positioned yet (NaN anchor → Empty bounds),
        // return Empty so the node pair is tracked but not indexed in the spatial grid.
        // OnNodeBoundsChanged will promote it when both endpoints have real coordinates.
        if (bA.IsEmpty || bB.IsEmpty)
            return Viewport.Empty;

        return Viewport.Union(bA, bB);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _providerA.PropertyChanged -= OnNodeBoundsChanged;
        _providerB.PropertyChanged -= OnNodeBoundsChanged;
    }
}
