using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class ViewManager
{
    private readonly Layout _layout;
    private readonly Dictionary<Type, Queue<View>> _viewPool = [];
    private readonly List<ControlItem> _activeViews = [];
    private readonly List<object> _pendingViews = [];
    private readonly Dictionary<Type, DataTemplate> _templateMap = [];
    private INotifyCollectionChanged? _currentCollection;
    private IEnumerable<object>? _currentEnumerable;
    private bool _isSchedulingRender;

    /// <summary>Maximum views to create per batch tick. Matches WPF's incremental
    /// rendering strategy to avoid long UI freezes during initial load.</summary>
    private const int BatchSize = 8;

    /// <summary>Timer for incremental batch rendering on platforms without
    /// DispatcherPriority.Background (all MAUI targets).</summary>
    private IDispatcherTimer? _batchTimer;

    public ViewManager(Layout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _layout.SizeChanged += OnLayoutSizeChanged;
    }

    public void Attach(INotifyCollectionChanged collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (_currentCollection is not null)
        {
            _currentCollection.CollectionChanged -= OnCollectionChanged;
            ClearAllViews();
        }

        if (collection is not IEnumerable enumerable)
        {
            throw new ArgumentException("Collection must implement IEnumerable.", nameof(collection));
        }

        _currentCollection = collection;
        _currentEnumerable = enumerable.Cast<object>();
        _currentCollection.CollectionChanged += OnCollectionChanged;

        _pendingViews.Clear();
        _pendingViews.AddRange(_currentEnumerable);
        Log($"Attach: layout={_layout.GetType().Name}, items={_pendingViews.Count}, selector={ViewPool.GetTemplateSelector(_layout)?.GetType().Name ?? "null"}");
        ScheduleNextBatchRender();
    }

    public void Detach()
    {
        _layout.SizeChanged -= OnLayoutSizeChanged;

        _batchTimer?.Stop();
        _batchTimer = null;

        if (_currentCollection is not null)
        {
            _currentCollection.CollectionChanged -= OnCollectionChanged;
            _currentCollection = null;
            _currentEnumerable = null;
        }

        _layoutApplyQueued.Clear();
        ClearAllViews();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                {
                    foreach (object item in e.NewItems)
                    {
                        RemoveReference(_pendingViews, item);
                        _pendingViews.Add(item);
                    }

                    ScheduleNextBatchRender();
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                {
                    foreach (object item in e.OldItems)
                    {
                        RemoveReference(_pendingViews, item);
                        HideViewFor(item);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                ResetAllViews();
                if (_currentEnumerable is not null)
                {
                    _pendingViews.Clear();
                    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    foreach (var item in _currentEnumerable)
                    {
                        if (seen.Add(item))
                        {
                            _pendingViews.Add(item);
                        }
                    }

                    ScheduleNextBatchRender();
                }
                break;
        }
    }

    private void ScheduleNextBatchRender()
    {
        if (_isSchedulingRender || _pendingViews.Count == 0)
        {
            return;
        }

        _isSchedulingRender = true;

        // Use a dispatcher timer for incremental rendering instead of processing
        // all pending views in one batch. MAUI lacks WPF's DispatcherPriority.Background,
        // so a per-frame timer spreads view creation across multiple frames.
        // This prevents UI freezes when loading 100+ workflow nodes.
        _batchTimer?.Stop();
        _batchTimer = _layout.Dispatcher.CreateTimer();
        _batchTimer.Interval = TimeSpan.FromMilliseconds(16); // ≈1 frame
        _batchTimer.IsRepeating = false;
        _batchTimer.Tick += OnBatchTimerTick;
        _batchTimer.Start();
    }

    private void OnBatchTimerTick(object? sender, EventArgs e)
    {
        try
        {
            _batchTimer?.Stop();
            _batchTimer = null;
            _isSchedulingRender = false;

            var processed = 0;
            while (processed < BatchSize && _pendingViews.Count > 0)
            {
                var viewModel = _pendingViews[0];
                _pendingViews.RemoveAt(0);

                try
                {
                    AddOrReuseView(viewModel);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewManager] Failed to create view for {viewModel.GetType()}: {ex}");
                }

                processed++;
            }

            if (_pendingViews.Count > 0)
            {
                // More views to create — schedule the next batch.
                ScheduleNextBatchRender();
            }
            else if (!_slotSyncQueued)
            {
                // All views created — schedule a single deferred pass to sync
                // node slot layouts (equivalent to WPF's LayoutUpdated).
                // This avoids the per-node SizeChanged cascade that MAUI triggers.
                _slotSyncQueued = true;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _slotSyncQueued = false;
                    BatchSyncNodeSlots();
                });
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // IDispatcherTimer.Tick exceptions escape to WinUI's UnhandledException
            // if not caught.  This is a known MAUI/WinUI limitation (dotnet/maui #12245).
            System.Diagnostics.Debug.WriteLine($"[ViewManager] Batch error: {ex.Message}");
        }
    }

    private void BatchSyncNodeSlots()
    {
        foreach (var item in _activeViews)
        {
            if (item.ViewModel is IWorkflowNodeViewModel
                && item.View is ContentView nodeView)
            {
                WorkflowSlotLayoutBehavior.Refresh(nodeView);
            }
        }
    }

    private void AddOrReuseView(object viewModel)
    {
        // Use HashSet-style lookup for O(1) duplicate check.
        // _activeViews is a List, so we scan — but LinkBuilder deduplicates,
        // and the pending queue filters duplicates, so this is rarely triggered.
        if (_activeViews.Count > 0)
        {
            for (int i = 0; i < _activeViews.Count; i++)
            {
                if (ReferenceEquals(_activeViews[i].ViewModel, viewModel))
                {
                    Log($"AddOrReuseView.skip: {viewModel.GetType().Name}");
                    return;
                }
            }
        }

        var viewType = viewModel.GetType();
        View? view = null;

        if (_viewPool.TryGetValue(viewType, out var pool) && pool.Count > 0)
        {
            view = pool.Dequeue();
        }

        if (view is null)
        {
            var template = FindDataTemplate(viewModel)
                ?? throw new InvalidOperationException($"No DataTemplate found for type: {viewType.FullName}");

            view = (View?)template.CreateContent()
                ?? throw new InvalidOperationException($"DataTemplate returned null for {viewType.FullName}");

            view.ZIndex = viewModel is IWorkflowLinkViewModel ? -1 : 1;
            _layout.Children.Add(view);
        }
        else
        {
            // Pooled view is still a child of _layout (we don't remove on hide).
            // Just make it visible again and re-apply the data context.
            // Also clean any stale deferred-layout entry from its previous life.
            _layoutApplyQueued.Remove(view);
            view.IsVisible = true;
        }

        view.BindingContext = viewModel;
        ApplyLayout(viewModel, view);

        _activeViews.Add(new ControlItem(viewModel, view, SubscribeToLayoutChanges(viewModel, view)));
    }

    private void HideViewFor(object viewModel)
    {
        ControlItem? item = null;
        for (int i = 0; i < _activeViews.Count; i++)
        {
            if (ReferenceEquals(_activeViews[i].ViewModel, viewModel))
            {
                item = _activeViews[i];
                _activeViews.RemoveAt(i);
                break;
            }
        }

        if (item is null) return;

        // Clean up stale deferred-layout entries so the dictionary doesn't
        // accumulate View references over the lifetime of the workflow surface.
        _layoutApplyQueued.Remove(item.View);

        // Keep the view in _layout.Children (do NOT remove) to avoid
        // expensive MAUI layout recalculations. Just hide and unbind.
        item.View.BindingContext = null;
        item.View.IsVisible = false;
        item.View.ZIndex = -100;
        UnsubscribeFromLayoutChanges(item);

        if (!_viewPool.TryGetValue(viewModel.GetType(), out var pool))
        {
            pool = [];
            _viewPool[viewModel.GetType()] = pool;
        }

        pool.Enqueue(item.View);
    }

    private void ResetAllViews()
    {
        foreach (var item in _activeViews)
        {
            // Clean up stale deferred-layout entries.
            _layoutApplyQueued.Remove(item.View);

            item.View.BindingContext = null;
            item.View.IsVisible = false;
            item.View.ZIndex = -100;
            UnsubscribeFromLayoutChanges(item);

            if (!_viewPool.TryGetValue(item.ViewModel.GetType(), out var pool))
            {
                pool = [];
                _viewPool[item.ViewModel.GetType()] = pool;
            }

            pool.Enqueue(item.View);
        }

        _activeViews.Clear();
    }

    private void ClearAllViews()
    {
        ResetAllViews();
        _pendingViews.Clear();
    }

    private bool _isLayoutApplying; // re-entrancy guard for SizeChanged cycle
    private readonly Dictionary<View, bool> _layoutApplyQueued = [];
    private bool _slotSyncQueued;

    private void OnLayoutSizeChanged(object? sender, EventArgs e)
    {
        if (_isLayoutApplying) return;
        _isLayoutApplying = true;
        try
        {
            foreach (var item in _activeViews)
            {
                ApplyLayout(item.ViewModel, item.View);
            }
        }
        finally
        {
            _isLayoutApplying = false;
        }
    }

    private PropertyChangedEventHandler? SubscribeToLayoutChanges(object viewModel, View view)
    {
        if (viewModel is not INotifyPropertyChanged notify)
        {
            return null;
        }

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
                or nameof(IWorkflowNodeViewModel.Size)
                or nameof(IWorkflowLinkViewModel.Sender)
                or nameof(IWorkflowLinkViewModel.Receiver)
                or nameof(IWorkflowSlotViewModel.Anchor))
            {
                // Coalesce: only dispatch ApplyLayout once per view per frame,
                // preventing runaway cascade when layout changes trigger more
                // PropertyChanged events which trigger more layout changes...
                if (_layoutApplyQueued.TryGetValue(view, out var queued) && queued)
                    return;
                _layoutApplyQueued[view] = true;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _layoutApplyQueued[view] = false;
                    ApplyLayout(viewModel, view);
                });
            }
        };

        notify.PropertyChanged += handler;
        return handler;
    }

    private static void UnsubscribeFromLayoutChanges(ControlItem item)
    {
        if (item.Handler is not null && item.ViewModel is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= item.Handler;
        }
    }

    private void ApplyLayout(object viewModel, View view)
    {
        if (_layout is not AbsoluteLayout canvas)
        {
            return;
        }

        switch (viewModel)
        {
            case IWorkflowNodeViewModel node:
                AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
                AbsoluteLayout.SetLayoutBounds(view, new Rect(node.Anchor.Horizontal, node.Anchor.Vertical, Math.Max(1, node.Size.Width), Math.Max(1, node.Size.Height)));
                break;
            case IWorkflowLinkViewModel:
                AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
                AbsoluteLayout.SetLayoutBounds(view, new Rect(0, 0, Math.Max(1, GetCanvasExtent(canvas.WidthRequest, canvas.Width)), Math.Max(1, GetCanvasExtent(canvas.HeightRequest, canvas.Height))));
                // ContentOffset cancels out Canvas.TranslationX in the link's
                // draw math.  Setting it here avoids {x:Reference} bindings in
                // XAML DataTemplate which cause WinUI binding exceptions.
                SyncLinkContentOffset(view);
                break;
            default:
                AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
                AbsoluteLayout.SetLayoutBounds(view, new Rect(0, 0, view.WidthRequest > 0 ? view.WidthRequest : -1, view.HeightRequest > 0 ? view.HeightRequest : -1));
                break;
        }
    }

    /// <summary>
    /// Synchronizes ContentOffsetX/Y on link views to match the canvas offset,
    /// so the link's draw coordinates match the node positions after TranslationX.
    /// Without this, links draw at world coordinates while nodes are shifted by
    /// Canvas.TranslationX, causing a visual offset = partial clipping.
    /// </summary>
    private static void SyncLinkContentOffset(View view)
    {
        Element? current = view;
        while (current is not null)
        {
            if (current.BindingContext is IWorkflowTreeViewModel tree)
            {
                var offset = tree.Layout.ActualOffset;
                // PolylineCurveView uses ContentOffsetX/Y in its draw math AND
                // sets TranslationX = -ContentOffsetX for visual offset.
                // For GraphicsView subclasses, set via BindableProperty.
                if (view is IWorkflowLinkRenderView linkView)
                {
                    linkView.ContentOffsetX = offset.Horizontal;
                    linkView.ContentOffsetY = offset.Vertical;
                }
                return;
            }
            current = current.Parent;
        }
    }


    private static double GetCanvasExtent(double requested, double actual)
        => requested > 0 ? requested : actual;

    private DataTemplate? FindDataTemplate(object context)
    {
        var contextType = context.GetType();
        if (_templateMap.TryGetValue(contextType, out var cached))
        {
            Log($"FindDataTemplate.cache: vm={contextType.Name}, template={cached.GetType().Name}");
            return cached;
        }

        var selector = ViewPool.GetTemplateSelector(_layout);
        if (selector?.SelectTemplate(context, _layout) is DataTemplate selected)
        {
            _templateMap[contextType] = selected;
            Log($"FindDataTemplate.selector: vm={contextType.Name}, selector={selector.GetType().Name}, template={selected.GetType().Name}");
            return selected;
        }

        if (TryFindTemplateByResourceKey(context, out var resourceTemplate) && resourceTemplate is not null)
        {
            _templateMap[contextType] = resourceTemplate;
            Log($"FindDataTemplate.resource: vm={contextType.Name}, template={resourceTemplate.GetType().Name}");
            return resourceTemplate;
        }

        Log($"FindDataTemplate.miss: vm={contextType.Name}");
        return null;
    }

    private bool TryFindTemplateByResourceKey(object context, out DataTemplate? template)
    {
        template = null;
        var resourceKey = (string?)null;

        if (resourceKey is null)
        {
            return false;
        }

        for (Element? current = _layout; current is not null; current = current.Parent)
        {
            if (current is VisualElement visualElement
                && visualElement.Resources.TryGetValue(resourceKey, out var resource)
                && resource is DataTemplate resourceTemplate)
            {
                template = resourceTemplate;
                Log($"TryFindTemplateByResourceKey.hitLocal: key={resourceKey}, owner={visualElement.GetType().Name}");
                return true;
            }
        }

        if (Application.Current?.Resources.TryGetValue(resourceKey, out var appResource) == true && appResource is DataTemplate appTemplate)
        {
            template = appTemplate;
            Log($"TryFindTemplateByResourceKey.hitApp: key={resourceKey}");
            return true;
        }

        Log($"TryFindTemplateByResourceKey.miss: key={resourceKey}");
        return false;
    }

    private static void RemoveReference(List<object> list, object item)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(list[i], item))
                list.RemoveAt(i);
        }
    }

    [Conditional("DEBUG")]
    private static void Log(string message)
        => Debug.WriteLine($"[ViewManager] {message}");

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class ControlItem
    {
        public object ViewModel { get; }
        public View View { get; }
        public PropertyChangedEventHandler? Handler { get; }

        public ControlItem(object viewModel, View view, PropertyChangedEventHandler? handler)
        {
            ViewModel = viewModel;
            View = view;
            Handler = handler;
        }
    }
}
