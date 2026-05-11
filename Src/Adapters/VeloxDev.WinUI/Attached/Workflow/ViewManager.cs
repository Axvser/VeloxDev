using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class ViewManager(Panel panel)
{
    private readonly Panel _panel = panel ?? throw new ArgumentNullException(nameof(panel));
    private readonly Dictionary<Type, Queue<FrameworkElement>> _viewPool = [];
    private readonly List<ControlItem> _activeViews = [];
    private readonly List<object> _pendingViews = [];
    private readonly Dictionary<Type, DataTemplate> _templateMap = [];
    private INotifyCollectionChanged? _currentCollection;
    private IEnumerable<object>? _currentEnumerable;
    private DataTemplateSelector? _templateSelector;
    private bool _isSchedulingRender;

    public void SetTemplateSelector(DataTemplateSelector? templateSelector)
    {
        _templateSelector = templateSelector;
        _templateMap.Clear();
    }

    public void Attach(INotifyCollectionChanged collection)
    {
        if (collection is not IEnumerable enumerable)
        {
            throw new ArgumentException("Collection must implement IEnumerable.", nameof(collection));
        }

        Detach();

        _currentCollection = collection;
        _currentEnumerable = enumerable.Cast<object>();
        _currentCollection.CollectionChanged += OnCollectionChanged;

        _pendingViews.Clear();
        _pendingViews.AddRange(_currentEnumerable);
        ScheduleNextBatchRender();
    }

    public void Detach()
    {
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
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                {
                    foreach (object item in e.NewItems)
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
                    foreach (object item in e.OldItems)
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
        _panel.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ProcessNextBatch);
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
                System.Diagnostics.Debug.WriteLine($"Failed to create view for {viewModel?.GetType()}: {ex}");
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
            return;
        }

        var viewType = viewModel.GetType();
        FrameworkElement? view = null;

        if (_viewPool.TryGetValue(viewType, out var pool) && pool.Count > 0)
        {
            view = pool.Dequeue();
        }

        if (view is null)
        {
            var template = FindDataTemplate(viewModel)
                ?? throw new InvalidOperationException($"No DataTemplate found for type: {viewType.FullName}");

            view = template.LoadContent() as FrameworkElement
                ?? throw new InvalidOperationException($"DataTemplate returned null for {viewType.FullName}");

            _panel.Children.Add(view);
        }

        ApplyLayout(view, viewModel);
        view.Visibility = Visibility.Visible;
        view.DataContext = viewModel;
        _activeViews.Add(new ControlItem(viewModel, view, SubscribeToLayoutChanges(viewModel, view)));
    }

    private void HideViewFor(object viewModel)
    {
        var item = _activeViews.FirstOrDefault(x => ReferenceEquals(x.ViewModel, viewModel));
        if (item?.View is not FrameworkElement view)
        {
            return;
        }

        view.Visibility = Visibility.Collapsed;
        view.DataContext = null;
        UnsubscribeFromLayoutChanges(item);

        var type = viewModel.GetType();
        if (!_viewPool.TryGetValue(type, out var pool))
        {
            pool = new Queue<FrameworkElement>();
            _viewPool[type] = pool;
        }

        pool.Enqueue(view);
        _activeViews.RemoveAll(x => ReferenceEquals(x.ViewModel, viewModel));
    }

    private void ResetAllViews()
    {
        foreach (var item in _activeViews)
        {
            item.View.Visibility = Visibility.Collapsed;
            item.View.DataContext = null;
            UnsubscribeFromLayoutChanges(item);

            var type = item.ViewModel.GetType();
            if (!_viewPool.TryGetValue(type, out var pool))
            {
                pool = new Queue<FrameworkElement>();
                _viewPool[type] = pool;
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

    private DataTemplate? FindDataTemplate(object context)
    {
        var contextType = context.GetType();
        if (_templateMap.TryGetValue(contextType, out var cached))
        {
            return cached;
        }

        if (_templateSelector is not null)
        {
            var presenter = new ContentPresenter { Content = context };
            if (_templateSelector.SelectTemplate(context, presenter) is DataTemplate selected)
            {
                _templateMap[contextType] = selected;
                return selected;
            }
        }

        for (FrameworkElement? current = _panel as FrameworkElement; current is not null; current = current.Parent as FrameworkElement)
        {
            foreach (var entry in current.Resources)
            {
                if (entry.Value is DataTemplate dataTemplate && IsMatchingTemplateKey(entry.Key, contextType))
                {
                    _templateMap[contextType] = dataTemplate;
                    return dataTemplate;
                }
            }
        }

        if (Application.Current is not null)
        {
            foreach (var entry in Application.Current.Resources)
            {
                if (entry.Value is DataTemplate dataTemplate && IsMatchingTemplateKey(entry.Key, contextType))
                {
                    _templateMap[contextType] = dataTemplate;
                    return dataTemplate;
                }
            }
        }

        return null;
    }

    private static PropertyChangedEventHandler? SubscribeToLayoutChanges(object viewModel, FrameworkElement view)
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
                if (view.DispatcherQueue.HasThreadAccess)
                {
                    ApplyLayout(view, viewModel);
                    return;
                }

                view.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ApplyLayout(view, viewModel));
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

    private static bool IsMatchingTemplateKey(object key, Type contextType)
        => key switch
        {
            string name => string.Equals(name, contextType.Name, StringComparison.Ordinal)
                || string.Equals(name, contextType.FullName, StringComparison.Ordinal)
                || string.Equals(name, GetTemplateKeyName(contextType), StringComparison.Ordinal),
            Type typeKey => typeKey == contextType,
            _ => false,
        };

    private static string GetTemplateKeyName(Type contextType)
        => contextType.Name.EndsWith("ViewModel", StringComparison.Ordinal)
            ? contextType.Name[..^"ViewModel".Length] + "Template"
            : contextType.Name + "Template";

    private static void ApplyLayout(FrameworkElement view, object viewModel)
    {
        switch (viewModel)
        {
            case IWorkflowNodeViewModel node:
                Canvas.SetLeft(view, node.Anchor.Horizontal);
                Canvas.SetTop(view, node.Anchor.Vertical);
                Canvas.SetZIndex(view, node.Anchor.Layer);
                view.Width = Math.Max(0, node.Size.Width);
                view.Height = Math.Max(0, node.Size.Height);
                break;
            case IWorkflowLinkViewModel:
                Canvas.SetLeft(view, 0);
                Canvas.SetTop(view, 0);
                break;
        }
    }

    private sealed class ControlItem(object viewModel, FrameworkElement view, PropertyChangedEventHandler? handler)
    {
        public object ViewModel { get; } = viewModel;
        public FrameworkElement View { get; } = view;
        public PropertyChangedEventHandler? Handler { get; } = handler;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
