using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo;

public class VirtualizingCanvas : Canvas
{
    private readonly Dictionary<Type, Queue<Control>> _viewPool = [];
    private readonly List<ControlItem> _activeViews = [];
    private ObservableCollection<IWorkflowViewModel>? _currentCollection;

    // 不再有 ItemsSource 属性！直接使用 DataContext

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // 1. 取消旧集合监听
        if (_currentCollection != null)
        {
            _currentCollection.CollectionChanged -= OnCollectionChanged;
            ClearAllViews();
        }

        // 2. 尝试将新 DataContext 转为集合
        if (DataContext is ObservableCollection<IWorkflowViewModel> newCollection)
        {
            _currentCollection = newCollection;
            _currentCollection.CollectionChanged += OnCollectionChanged;

            // 初始加载所有项
            foreach (var item in _currentCollection)
            {
                AddOrReuseView(item);
            }
        }
        else
        {
            _currentCollection = null;
            if (DataContext != null)
            {
                throw new InvalidOperationException(
                    $"DataContext must be ObservableCollection<IWorkflowViewModel>, but got {DataContext.GetType().FullName}");
            }
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (IWorkflowViewModel item in e.NewItems!)
                    AddOrReuseView(item);
                break;

            case NotifyCollectionChangedAction.Remove:
                foreach (IWorkflowViewModel item in e.OldItems!)
                    HideViewFor(item);
                break;

            case NotifyCollectionChangedAction.Reset:
                ResetAllViews();
                break;
        }
    }

    private void AddOrReuseView(IWorkflowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var viewType = viewModel.GetType();
        Control? view = null;

        if (_viewPool.TryGetValue(viewType, out var pool) && pool.Count > 0)
        {
            view = pool.Dequeue();
        }

        if (view == null)
        {
            if (!FindDataTemplate(viewModel, out var template))
            {
                throw new InvalidOperationException(
                    $"No DataTemplate found for ViewModel type: {viewType.FullName}");
            }

            view = (Control?)template!.Build(null);
            if (view == null)
            {
                throw new InvalidOperationException(
                    $"DataTemplate.Build() returned null for {viewType.FullName}");
            }

            Children.Add(view);
        }

        view.DataContext = viewModel;
        view.IsVisible = true;
        _activeViews.Add(new ControlItem { ViewModel = viewModel, View = view });
    }

    private void HideViewFor(IWorkflowViewModel viewModel)
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

        if (_currentCollection != null)
        {
            foreach (var vm in _currentCollection)
                AddOrReuseView(vm);
        }
    }

    private void ClearAllViews()
    {
        ResetAllViews();
    }

    private record ControlItem
    {
        public IWorkflowViewModel ViewModel { get; init; } = null!;
        public Control View { get; init; } = null!;
    }

    private bool FindDataTemplate(object context, out IDataTemplate? template)
    {
        if (_templateMap.TryGetValue(context.GetType(), out template)) return true;

        if (this.DataTemplates?.Count > 0)
        {
            foreach (var dt in this.DataTemplates)
                if (dt.Match(context))
                {
                    template = dt;
                    _templateMap[context.GetType()] = dt;
                    return true;
                }
        }

        var parent = this.Parent;
        while (parent != null)
        {
            if (parent is Control control && control.DataTemplates?.Count > 0)
            {
                foreach (var dt in control.DataTemplates)
                    if (dt.Match(context))
                    {
                        template = dt;
                        _templateMap[context.GetType()] = dt;
                        return true;
                    }
            }
            parent = parent.Parent;
        }

        if (Application.Current?.DataTemplates is { Count: > 0 } appTemplates)
        {
            foreach (var dt in appTemplates)
                if (dt.Match(context))
                {
                    template = dt;
                    _templateMap[context.GetType()] = dt;
                    return true;
                }
        }

        template = null;
        return false;
    }

    private readonly Dictionary<Type, IDataTemplate> _templateMap = [];
}