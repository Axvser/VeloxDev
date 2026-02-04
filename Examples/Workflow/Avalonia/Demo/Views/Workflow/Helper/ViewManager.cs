using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Demo;

public sealed class ViewManager(Panel panel)
{
    private readonly Panel _panel = panel ?? throw new ArgumentNullException(nameof(panel));
    private readonly Dictionary<Type, Queue<Control>> _viewPool = [];
    private readonly List<ControlItem> _activeViews = [];
    private readonly List<object> _pendingViews = [];
    private INotifyCollectionChanged? _currentCollection;
    private IEnumerable<object>? _currentEnumerable;
    private bool _isSchedulingRender = false;
    private readonly Dictionary<Type, IDataTemplate> _templateMap = [];

    public void Attach(INotifyCollectionChanged collection)
    {
        if (_currentCollection != null)
        {
            _currentCollection.CollectionChanged -= OnCollectionChanged;
            ClearAllViews();
        }

        // 检查是否可枚举（必须！）
        if (collection is not IEnumerable enumerable)
            throw new ArgumentException("Collection must implement IEnumerable to support enumeration.", nameof(collection));

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
                    foreach (object item in e.NewItems)
                    {
                        _pendingViews.RemoveAll(x => ReferenceEquals(x, item));
                        _pendingViews.Add(item);
                    }
                    ScheduleNextBatchRender();
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (object item in e.OldItems)
                        HideViewFor(item);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                ResetAllViews();
                if (_currentEnumerable != null)
                {
                    _pendingViews.Clear();
                    var seen = new HashSet<object>(ReferenceEqualityComparer<object>.Instance);
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

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private void ScheduleNextBatchRender()
    {
        if (_isSchedulingRender || _pendingViews.Count == 0) return;
        _isSchedulingRender = true;
        Dispatcher.UIThread.Post(ProcessNextBatch, DispatcherPriority.Background);
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
        Control? view = null;

        if (_viewPool.TryGetValue(viewType, out var pool) && pool.Count > 0)
            view = pool.Dequeue();

        if (view == null)
        {
            if (!FindDataTemplate(viewModel, out var template))
                throw new InvalidOperationException($"No DataTemplate found for type: {viewType.FullName}");

            view = (Control?)template?.Build(null);
            if (view == null)
                throw new InvalidOperationException($"DataTemplate returned null for {viewType.FullName}");

            _panel.Children.Add(view);
        }

        view.DataContext = viewModel;
        view.IsVisible = true;
        _activeViews.Add(new ControlItem { ViewModel = viewModel, View = view });
    }

    private void HideViewFor(object viewModel)
    {
        var item = _activeViews.FirstOrDefault(x => ReferenceEquals(x.ViewModel, viewModel));
        if (item?.View is { } view)
        {
            view.IsVisible = false;
            view.DataContext = null;

            var type = viewModel.GetType();
            if (!_viewPool.TryGetValue(type, out var pool))
            {
                pool = new Queue<Control>();
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
                item.View.IsVisible = false;
                item.View.DataContext = null;

                if (item.ViewModel?.GetType() is { } type)
                {
                    if (!_viewPool.TryGetValue(type, out var pool))
                    {
                        pool = new Queue<Control>();
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

    private bool FindDataTemplate(object context, out IDataTemplate? template)
    {
        var contextType = context.GetType();
        if (_templateMap.TryGetValue(contextType, out template)) return true;

        if (TryFindTemplateIn(_panel.DataTemplates, context, out template))
        {
            _templateMap[contextType] = template!;
            return true;
        }

        var parent = _panel.Parent;
        while (parent is Control control)
        {
            if (TryFindTemplateIn(control.DataTemplates, context, out template))
            {
                _templateMap[contextType] = template!;
                return true;
            }
            parent = parent.Parent;
        }

        if (Application.Current?.DataTemplates is { Count: > 0 } appTemplates &&
            TryFindTemplateIn(appTemplates, context, out template))
        {
            _templateMap[contextType] = template!;
            return true;
        }

        template = null;
        return false;
    }

    private static bool TryFindTemplateIn(IList<IDataTemplate>? templates, object context, out IDataTemplate? template)
    {
        template = null;
        if (templates == null) return false;
        foreach (var dt in templates)
        {
            if (dt.Match(context))
            {
                template = dt;
                return true;
            }
        }
        return false;
    }

    private record ControlItem
    {
        public object ViewModel { get; init; } = null!;
        public Control View { get; init; } = null!;
    }
}