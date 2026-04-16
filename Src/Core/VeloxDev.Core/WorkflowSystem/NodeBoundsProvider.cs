using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A wrapper that provides spatial bounds for a workflow node.
/// This enables nodes to participate in the generic <see cref="SpatialGridHashMap{T}"/> spatial indexing.
/// </summary>
internal sealed class NodeBoundsProvider : ISpatialBoundsProvider, IDisposable
{
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bounds)));
        }
    }

    private Viewport GetCurrentBounds()
        => new(_node.Anchor.Horizontal, _node.Anchor.Vertical, _node.Size.Width, _node.Size.Height);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unsubscribe();
    }
}
