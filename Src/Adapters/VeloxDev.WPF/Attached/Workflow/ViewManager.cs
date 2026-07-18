using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class ViewManager(Panel panel)
{
    private readonly Panel _panel = panel ?? throw new ArgumentNullException(nameof(panel));
    private readonly Dictionary<Type, Queue<FrameworkElement>> _viewPool = [];
    private readonly List<ControlItem> _activeViews = [];
    private readonly List<object> _pendingViews = [];
    private INotifyCollectionChanged? _currentCollection;
    private IEnumerable<object>? _currentEnumerable;
    private bool _isSchedulingRender = false;
    private readonly Dictionary<Type, DataTemplate> _templateMap = [];

    public void Attach(INotifyCollectionChanged collection)
    {
        if (_currentCollection != null)
        {
            _currentCollection.CollectionChanged -= OnCollectionChanged;
            ClearAllViews();
        }

        if (collection is not IEnumerable enumerable)
            throw new ArgumentException("Collection must implement IEnumerable.", nameof(collection));

        _currentCollection = collection;
        _currentEnumerable = enumerable.Cast<object>();
        _currentCollection.CollectionChanged += OnCollectionChanged;

        _pendingViews.Clear();
        _pendingViews.AddRange(_currentEnumerable);
        ScheduleNextBatchRender();
    }

    public void Detach()
    {
        if (_currentCollection != null)
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
                if (e.NewItems != null)
                {
                    foreach (var item in e.NewItems.OfType<object>())
                    {
                        RemoveReference(_pendingViews, item);
                        _pendingViews.Add(item);
                    }
                    ScheduleNextBatchRender();
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems.OfType<object>())
                    {
                        RemoveReference(_pendingViews, item);
                        HideViewFor(item);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                ResetAllViews();
                if (_currentEnumerable != null)
                {
                    _pendingViews.Clear();
                    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    foreach (var item in _currentEnumerable)
                    {
                        if (seen.Add(item))
                            _pendingViews.Add(item);
                    }
                    ScheduleNextBatchRender();
                }
                break;
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private static void RemoveReference(List<object> list, object item)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(list[i], item))
                list.RemoveAt(i);
        }
    }

    private void ScheduleNextBatchRender()
    {
        if (_isSchedulingRender || _pendingViews.Count == 0) return;
        _isSchedulingRender = true;
        _panel.Dispatcher.BeginInvoke(ProcessNextBatch, DispatcherPriority.Background);
    }

    private void ProcessNextBatch()
    {
        _isSchedulingRender = false;
        const int batchSize = 3;
        int processed = 0;

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
            ScheduleNextBatchRender();
    }

    private void AddOrReuseView(object viewModel)
    {
        if (_activeViews.Any(x => ReferenceEquals(x.ViewModel, viewModel)))
        {
            System.Diagnostics.Debug.WriteLine($"Warning: ViewModel already active: {viewModel}");
            return;
        }

        var viewType = viewModel.GetType();
        FrameworkElement? view = null;

        if (_viewPool.TryGetValue(viewType, out var pool) && pool.Count > 0)
            view = pool.Dequeue();

        if (view == null)
        {
            var template = FindDataTemplate(viewModel)
                ?? throw new InvalidOperationException($"No DataTemplate found for type: {viewType.FullName}");

            view = (FrameworkElement?)template.LoadContent()
                ?? throw new InvalidOperationException($"DataTemplate returned null for {viewType.FullName}");

            _panel.Children.Add(view);
        }

        view.Visibility = Visibility.Visible;
        view.DataContext = viewModel;
        _activeViews.Add(new ControlItem { ViewModel = viewModel, View = view });
    }

    private void HideViewFor(object viewModel)
    {
        var item = _activeViews.FirstOrDefault(x => ReferenceEquals(x.ViewModel, viewModel));
        if (item?.View is { } view)
        {
            view.Visibility = Visibility.Collapsed;
            view.DataContext = null;

            var type = viewModel.GetType();
            if (!_viewPool.TryGetValue(type, out var pool))
            {
                pool = new Queue<FrameworkElement>();
                _viewPool[type] = pool;
            }
            pool.Enqueue(view);
        }

        _activeViews.RemoveAll(x => ReferenceEquals(x.ViewModel, viewModel));
    }

    private void ResetAllViews()
    {
        foreach (var item in _activeViews)
        {
            if (item.View != null)
            {
                item.View.Visibility = Visibility.Collapsed;
                item.View.DataContext = null;

                if (item.ViewModel?.GetType() is { } type)
                {
                    if (!_viewPool.TryGetValue(type, out var pool))
                    {
                        pool = new Queue<FrameworkElement>();
                        _viewPool[type] = pool;
                    }
                    pool.Enqueue(item.View);
                }
            }
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
            return cached;

        var selector = ViewPool.GetTemplateSelector(_panel);
        if (selector != null)
        {
            // 构造一个临时 ContentPresenter 作为 container 参数
            var cp = new ContentPresenter { Content = context };
            if (selector.SelectTemplate(context, cp) is DataTemplate selected)
            {
                _templateMap[contextType] = selected;
                return selected;
            }
        }

        // 沿视觉树向上查找 DataTemplate（Resources 字典）
        DependencyObject? current = _panel;
        while (current != null)
        {
            if (current is FrameworkElement fe)
            {
                foreach (var key in fe.Resources.Keys)
                {
                    if (fe.Resources[key] is DataTemplate dt && dt.DataType is Type dtType && dtType == contextType)
                    {
                        _templateMap[contextType] = dt;
                        return dt;
                    }
                }
            }
            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                   ?? (current is FrameworkElement fe2 ? fe2.Parent : null);
        }

        // 查找 Application 资源
        foreach (var key in Application.Current.Resources.Keys)
        {
            if (Application.Current.Resources[key] is DataTemplate dt && dt.DataType is Type dtType && dtType == contextType)
            {
                _templateMap[contextType] = dt;
                return dt;
            }
        }

        return null;
    }

    private sealed class ControlItem
    {
        public object ViewModel { get; set; }
        public FrameworkElement View { get; set; }

        public ControlItem()
        {
            ViewModel = new object();
            View = new FrameworkElement();
        }
    }
}
