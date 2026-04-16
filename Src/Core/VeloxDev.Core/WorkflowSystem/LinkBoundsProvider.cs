using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A wrapper that provides spatial bounds for a workflow link based on its sender and receiver positions.
/// This enables links to participate in spatial indexing independently of their connected nodes.
/// </summary>
public sealed class LinkBoundsProvider : ISpatialBoundsProvider, IDisposable
{
    private readonly IWorkflowLinkViewModel _link;
    private Viewport _cachedBounds;
    private IWorkflowSlotViewModel? _trackedSender;
    private IWorkflowSlotViewModel? _trackedReceiver;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LinkBoundsProvider(IWorkflowLinkViewModel link)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _cachedBounds = CalculateBounds();
        SubscribeToLink();
    }

    public IWorkflowLinkViewModel Link => _link;

    public Viewport Bounds => _cachedBounds;

    private void SubscribeToLink()
    {
        if (_link is INotifyPropertyChanged linkNotifier)
        {
            linkNotifier.PropertyChanged += OnLinkPropertyChanged;
        }

        TrackSender(_link.Sender);
        TrackReceiver(_link.Receiver);
    }

    private void UnsubscribeFromLink()
    {
        if (_link is INotifyPropertyChanged linkNotifier)
        {
            linkNotifier.PropertyChanged -= OnLinkPropertyChanged;
        }

        UntrackSender();
        UntrackReceiver();
    }

    private void OnLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWorkflowLinkViewModel.Sender))
        {
            UntrackSender();
            TrackSender(_link.Sender);
            UpdateBounds();
        }
        else if (e.PropertyName == nameof(IWorkflowLinkViewModel.Receiver))
        {
            UntrackReceiver();
            TrackReceiver(_link.Receiver);
            UpdateBounds();
        }
    }

    private void TrackSender(IWorkflowSlotViewModel? slot)
    {
        _trackedSender = slot;
        if (_trackedSender is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnSlotPropertyChanged;
        }
    }

    private void UntrackSender()
    {
        if (_trackedSender is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged -= OnSlotPropertyChanged;
        }
        _trackedSender = null;
    }

    private void TrackReceiver(IWorkflowSlotViewModel? slot)
    {
        _trackedReceiver = slot;
        if (_trackedReceiver is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnSlotPropertyChanged;
        }
    }

    private void UntrackReceiver()
    {
        if (_trackedReceiver is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged -= OnSlotPropertyChanged;
        }
        _trackedReceiver = null;
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWorkflowSlotViewModel.Anchor))
        {
            UpdateBounds();
        }
    }

    private void UpdateBounds()
    {
        var newBounds = CalculateBounds();
        if (!_cachedBounds.Equals(newBounds))
        {
            _cachedBounds = newBounds;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bounds)));
        }
    }

    private Viewport CalculateBounds()
    {
        var senderAnchor = _link.Sender?.Anchor ?? new();
        var receiverAnchor = _link.Receiver?.Anchor ?? new();

        var minX = Math.Min(senderAnchor.Horizontal, receiverAnchor.Horizontal);
        var minY = Math.Min(senderAnchor.Vertical, receiverAnchor.Vertical);
        var maxX = Math.Max(senderAnchor.Horizontal, receiverAnchor.Horizontal);
        var maxY = Math.Max(senderAnchor.Vertical, receiverAnchor.Vertical);

        var width = Math.Max(maxX - minX, 1.0);
        var height = Math.Max(maxY - minY, 1.0);

        return new Viewport(minX, minY, width, height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnsubscribeFromLink();
    }
}
