using System.Collections;
using System.Collections.Specialized;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>A view-pooling manager that attaches to a collection and materializes one view per item into a host
/// panel, resolving each item's view through a <see cref="DataTemplateSelector"/> (or a resource-located
/// <see cref="DataTemplate"/>), reusing pooled instances by item type and hiding them again when items leave.
/// Mirrors the WPF adapter's ViewManager/ViewPool pairing.</summary>
/// <remarks>
/// Views are created in batches of three at <see cref="DispatcherPriority.Background"/> so a large collection
/// does not stall the first frame. Unlike WPF, the manager also handles <see cref="NotifyCollectionChangedAction.Replace"/>.
/// </remarks>
public sealed class ViewManager : IDisposable
{
    private readonly Panel _host;
    private readonly Dictionary<Type, Queue<FrameworkElement>> _pool = new();
    private readonly List<ViewItem> _active = new();
    private readonly List<object> _pending = new();
    private readonly Dictionary<Type, DataTemplate> _templateMap = new();
    private INotifyCollectionChanged? _collection;
    private IEnumerable<object>? _enumerable;
    private DataTemplateSelector? _selector;
    private bool _isSchedulingRender;

    /// <summary>Initializes a view manager that renders pooled item views into the host panel.</summary>
    public ViewManager(Panel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Sets the template selector used to materialize item views. Must be set before Attach.</summary>
    public void SetTemplateSelector(DataTemplateSelector selector)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    /// <summary>Binds the manager to a collection, replacing any previous binding.</summary>
    public void Attach(INotifyCollectionChanged collection)
    {
        Detach();

        if (collection is not IEnumerable enumerable)
        {
            throw new ArgumentException("Collection must implement IEnumerable.", nameof(collection));
        }

        _collection = collection;
        _enumerable = enumerable.Cast<object>();
        _collection.CollectionChanged += OnCollectionChanged;

        // 先排队再分批物化：大集合首帧只建三个，其余按 Background 优先级逐批补上。
        _pending.Clear();
        _pending.AddRange(_enumerable);
        ScheduleNextBatchRender();
    }

    /// <summary>Detaches from the collection and hides all pooled views.</summary>
    public void Detach()
    {
        if (_collection is not null)
        {
            _collection.CollectionChanged -= OnCollectionChanged;
            _collection = null;
        }

        _enumerable = null;
        ClearAllViews();
    }

    /// <inheritdoc />
    public void Dispose() => Detach();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                {
                    foreach (var item in e.NewItems.OfType<object>())
                    {
                        RemoveReference(_pending, item);
                        _pending.Add(item);
                    }
                    ScheduleNextBatchRender();
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems.OfType<object>())
                    {
                        RemoveReference(_pending, item);
                        HideViewFor(item);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Replace:
                // 本家既有差异：WPF 的 ViewManager 不处理 Replace，这里保留。
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems.OfType<object>())
                    {
                        RemoveReference(_pending, item);
                        HideViewFor(item);
                    }
                }
                if (e.NewItems is not null)
                {
                    foreach (var item in e.NewItems.OfType<object>())
                    {
                        RemoveReference(_pending, item);
                        _pending.Add(item);
                    }
                    ScheduleNextBatchRender();
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                ResetAllViews();
                if (_enumerable is not null)
                {
                    _pending.Clear();
                    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    foreach (var item in _enumerable)
                    {
                        if (seen.Add(item))
                        {
                            _pending.Add(item);
                        }
                    }
                    ScheduleNextBatchRender();
                }
                break;
        }
    }

    private static void RemoveReference(List<object> list, object item)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(list[i], item))
            {
                list.RemoveAt(i);
            }
        }
    }

    private void ScheduleNextBatchRender()
    {
        if (_isSchedulingRender || _pending.Count == 0) return;

        _isSchedulingRender = true;
        _host.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ProcessNextBatch));
    }

    private void ProcessNextBatch()
    {
        _isSchedulingRender = false;
        const int batchSize = 3;
        var processed = 0;

        while (processed < batchSize && _pending.Count > 0)
        {
            var item = _pending[0];
            _pending.RemoveAt(0);

            try
            {
                AddOrReuseView(item);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create view for {item.GetType()}: {ex}");
            }

            processed++;
        }

        if (_pending.Count > 0)
        {
            ScheduleNextBatchRender();
        }
    }

    private void AddOrReuseView(object item)
    {
        if (_active.Any(x => ReferenceEquals(x.Item, item)))
        {
            System.Diagnostics.Debug.WriteLine($"Warning: item already active: {item}");
            return;
        }

        var itemType = item.GetType();
        FrameworkElement view;

        if (_pool.TryGetValue(itemType, out var pool) && pool.Count > 0)
        {
            view = pool.Dequeue();
        }
        else
        {
            var template = FindDataTemplate(item)
                ?? throw new InvalidOperationException($"No DataTemplate found for type: {itemType.FullName}");

            view = template.LoadContent()
                ?? throw new InvalidOperationException($"DataTemplate returned null for {itemType.FullName}");
        }

        ApplyContext(view, item);
        view.Visibility = Visibility.Visible;
        if (!_host.Children.Contains(view))
        {
            _host.Children.Add(view);
        }

        _active.Add(new ViewItem { Item = item, View = view });
    }

    private void HideViewFor(object item)
    {
        var index = _active.FindIndex(x => ReferenceEquals(x.Item, item));
        if (index < 0)
        {
            return;
        }

        var view = _active[index].View;
        view.Visibility = Visibility.Collapsed;
        view.DataContext = null;

        PoolOf(item.GetType()).Enqueue(view);
        _active.RemoveAt(index);
    }

    private void ResetAllViews()
    {
        foreach (var item in _active)
        {
            item.View.Visibility = Visibility.Collapsed;
            item.View.DataContext = null;
            PoolOf(item.Item.GetType()).Enqueue(item.View);
        }

        _active.Clear();
    }

    private void ClearAllViews()
    {
        ResetAllViews();
        _pending.Clear();
    }

    private Queue<FrameworkElement> PoolOf(Type itemType)
    {
        if (!_pool.TryGetValue(itemType, out var pool))
        {
            pool = new Queue<FrameworkElement>();
            _pool[itemType] = pool;
        }

        return pool;
    }

    // 三级回退（与 WPF 同形）：选择器 → 沿视觉树上溯元素的 Resources → Application.Resources。
    // 按 Type 缓存解析结果，避免每个 item 都重走一遍资源树。
    private DataTemplate? FindDataTemplate(object item)
    {
        var itemType = item.GetType();
        if (_templateMap.TryGetValue(itemType, out var cached))
        {
            return cached;
        }

        if (_selector is not null)
        {
            // 临时 ContentPresenter 只作容器参数，不参与视觉树。
            var container = new ContentPresenter { Content = item };
            if (_selector.SelectTemplate(item, container) is DataTemplate selected)
            {
                _templateMap[itemType] = selected;
                return selected;
            }
        }

        DependencyObject? current = _host;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                foreach (var key in element.Resources.Keys)
                {
                    if (element.Resources[key] is DataTemplate template
                        && template.DataType is Type dataType
                        && dataType == itemType)
                    {
                        _templateMap[itemType] = template;
                        return template;
                    }
                }
            }

            current = VisualTreeHelper.GetParent(current)
                ?? (current is FrameworkElement parent ? parent.Parent : null);
        }

        if (Application.Current is { } application)
        {
            foreach (var key in application.Resources.Keys)
            {
                if (application.Resources[key] is DataTemplate template
                    && template.DataType is Type dataType
                    && dataType == itemType)
                {
                    _templateMap[itemType] = template;
                    return template;
                }
            }
        }

        return null;
    }

    private static void ApplyContext(FrameworkElement view, object item)
    {
        view.DataContext = item;
    }

    private sealed class ViewItem
    {
        public object Item { get; init; } = null!;
        public FrameworkElement View { get; init; } = null!;
    }
}
