using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A wrapper that provides spatial bounds for a workflow node.
/// This enables nodes to participate in the generic <see cref="SpatialGridHashMap{T}"/> spatial indexing.
/// </summary>
internal sealed class NodeBoundsProvider : ISpatialBoundsProvider, IDisposable
{
    private static readonly PropertyChangedEventArgs BoundsChangedEventArgs = new(nameof(Bounds));

    private readonly IWorkflowNodeViewModel _node;
    private Viewport _cachedBounds;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal NodeBoundsProvider(IWorkflowNodeViewModel node)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _cachedBounds = GetCurrentBounds();
        Subscribe();
    }

    /// <summary>
    /// Gets the associated node view model.
    /// </summary>
    internal IWorkflowNodeViewModel Node => _node;

    /// <inheritdoc/>
    public Viewport Bounds => _cachedBounds;

    private void Subscribe()
    {
        if (_node is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void Unsubscribe()
    {
        if (_node is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWorkflowNodeViewModel.Anchor) ||
            e.PropertyName == nameof(IWorkflowNodeViewModel.Size))
        {
            UpdateBounds();
        }
    }

    private void UpdateBounds()
    {
        var newBounds = GetCurrentBounds();
        if (!_cachedBounds.Equals(newBounds))
        {
            _cachedBounds = newBounds;
            PropertyChanged?.Invoke(this, BoundsChangedEventArgs);
        }
    }

    private Viewport GetCurrentBounds()
    {
        var h = _node.Anchor.Horizontal;
        var v = _node.Anchor.Vertical;
        // Anchor defaults to NaN before the view layer positions the node.
        // Return empty bounds so the spatial grid places this in the pending
        // queue rather than indexing at NaN coordinates.
        if (double.IsNaN(h) || double.IsNaN(v) ||
            double.IsInfinity(h) || double.IsInfinity(v))
        {
            return Viewport.Empty;
        }
        return new Viewport(h, v, _node.Size.Width, _node.Size.Height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unsubscribe();
    }
}
