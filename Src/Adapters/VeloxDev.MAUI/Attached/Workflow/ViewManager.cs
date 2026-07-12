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

    public ViewManager(Layout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _layout.SizeChanged += OnLayoutSizeChanged;

        if (_layout is VisualElement visualElement)
        {
            visualElement.PropertyChanged += OnLayoutPropertyChanged;
        }
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

        if (_layout is VisualElement visualElement)
        {
            visualElement.PropertyChanged -= OnLayoutPropertyChanged;
        }

        if (_currentCollection is not null)
        {
            _currentCollection.CollectionChanged -= OnCollectionChanged;
            _currentCollection = null;
            _currentEnumerable = null;
        }

        ClearAllViews();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Log($"CollectionChanged: action={e.Action}, new={e.NewItems?.Count ?? 0}, old={e.OldItems?.Count ?? 0}, active={_activeViews.Count}, pending={_pendingViews.Count}");
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                {
                    foreach (var item in e.NewItems.Cast<object>())
                    {
                        _pendingViews.RemoveAll(x => ReferenceEquals(x, item));
                        _pendingViews.Add(item);
                    }

                    ScheduleNextBatchRender();
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems.Cast<object>())
                    {
                        _pendingViews.RemoveAll(x => ReferenceEquals(x, item));
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
        MainThread.BeginInvokeOnMainThread(ProcessNextBatch);
    }

    private void ProcessNextBatch()
    {
        _isSchedulingRender = false;
        const int batchSize = 3;
        var processed = 0;

        while (processed < batchSize && _pendingViews.Count > 0)
        {
            var viewModel = _pendingViews[0];
            _pendingViews.RemoveAt(0);

            try
            {
                AddOrReuseView(viewModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create view for {viewModel.GetType()}: {ex}");
            }

            processed++;
        }

        if (_pendingViews.Count > 0)
        {
            ScheduleNextBatchRender();
        }
    }

    private void AddOrReuseView(object viewModel)
    {
        if (_activeViews.Any(x => ReferenceEquals(x.ViewModel, viewModel)))
        {
            Log($"AddOrReuseView.skip: {viewModel.GetType().Name}");
            return;
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
            Log($"AddOrReuseView.template: vm={viewModel.GetType().Name}, template={template.GetType().Name}");
            view = (View?)template.CreateContent()
                ?? throw new InvalidOperationException($"DataTemplate returned null for {viewType.FullName}");
            Log($"AddOrReuseView.templateContent: vm={viewModel.GetType().Name}, view={view.GetType().Name}");

            _layout.Children.Add(view);
            Log($"AddOrReuseView.added: vm={viewModel.GetType().Name}, children={_layout.Children.Count}");
        }

        view.BindingContext = viewModel;
        view.IsVisible = true;
        ApplyLayout(viewModel, view);
        if (viewModel is IWorkflowNodeViewModel && view is ContentView nodeView)
        {
            WorkflowSlotLayoutBehavior.Refresh(nodeView);
        }
        view.ZIndex = viewModel is IWorkflowLinkViewModel ? -1 : 1;
        _activeViews.Add(new ControlItem(viewModel, view, SubscribeToLayoutChanges(viewModel, view)));
        Log($"AddOrReuseView.bound: vm={viewModel.GetType().Name}, view={view.GetType().Name}, bounds={AbsoluteLayout.GetLayoutBounds(view)}, active={_activeViews.Count}");
    }

    private void HideViewFor(object viewModel)
    {
        var item = _activeViews.FirstOrDefault(x => ReferenceEquals(x.ViewModel, viewModel));
        if (item is null)
        {
            return;
        }

        item.View.IsVisible = false;
        item.View.BindingContext = null;
        UnsubscribeFromLayoutChanges(item);
        Log($"HideViewFor: vm={viewModel.GetType().Name}, view={item.View.GetType().Name}");

        if (!_viewPool.TryGetValue(viewModel.GetType(), out var pool))
        {
            pool = [];
            _viewPool[viewModel.GetType()] = pool;
        }

        pool.Enqueue(item.View);
        _activeViews.RemoveAll(x => ReferenceEquals(x.ViewModel, viewModel));
    }

    private void ResetAllViews()
    {
        foreach (var item in _activeViews)
        {
            item.View.IsVisible = false;
            item.View.BindingContext = null;
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

    private void OnLayoutSizeChanged(object? sender, EventArgs e)
    {
        foreach (var item in _activeViews)
        {
            ApplyLayout(item.ViewModel, item.View);
        }
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(VisualElement.WidthRequest)
            and not nameof(VisualElement.HeightRequest))
        {
            return;
        }

        OnLayoutSizeChanged(sender, EventArgs.Empty);
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
                MainThread.BeginInvokeOnMainThread(() => ApplyLayout(viewModel, view));
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
                break;
            default:
                AbsoluteLayout.SetLayoutFlags(view, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
                AbsoluteLayout.SetLayoutBounds(view, new Rect(0, 0, view.WidthRequest > 0 ? view.WidthRequest : -1, view.HeightRequest > 0 ? view.HeightRequest : -1));
                break;
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
