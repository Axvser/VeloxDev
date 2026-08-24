using System.Collections;
using System.Collections.Specialized;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>View pooling manager (code-first). Attaches to a collection and materializes one
/// view per item into a host Panel, reusing pooled instances by item type and hiding them
/// again when items are removed. Mirrors the XAML adapters' ViewManager/ViewPool pairing.</summary>
public sealed class ViewManager : IDisposable
{
    private readonly Panel _host;
    private readonly Dictionary<Type, Queue<FrameworkElement>> _pool = new();
    private readonly List<ViewItem> _active = new();
    private INotifyCollectionChanged? _collection;
    private IWorkflowTemplateSelector? _selector;

    /// <summary>Initializes a view manager that renders pooled item views into the host panel.</summary>
    public ViewManager(Panel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Sets the template selector used to materialize item views. Must be set before Attach.</summary>
    public void SetTemplateSelector(IWorkflowTemplateSelector selector)
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
        _collection.CollectionChanged += OnCollectionChanged;

        foreach (var item in enumerable)
        {
            AddItem(item);
        }
    }

    /// <summary>Detaches from the collection and hides all pooled views.</summary>
    public void Detach()
    {
        if (_collection is not null)
        {
            _collection.CollectionChanged -= OnCollectionChanged;
            _collection = null;
        }

        ClearAll();
    }

    /// <inheritdoc />
    public void Dispose() => Detach();

    /// <summary>Pushes a render transform onto every active view (the canvas content translate).</summary>
    internal void UpdateRenderTransforms(Transform transform)
    {
        foreach (var item in _active)
        {
            item.View.RenderTransform = transform;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                ClearAll();
                if (_collection is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        AddItem(item);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems)
                    {
                        RemoveItem(item);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                {
                    foreach (var item in e.NewItems)
                    {
                        AddItem(item);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Replace:
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems)
                    {
                        RemoveItem(item);
                    }
                }
                if (e.NewItems is not null)
                {
                    foreach (var item in e.NewItems)
                    {
                        AddItem(item);
                    }
                }
                break;
        }
    }

    private void AddItem(object? item)
    {
        if (item is null || _selector is null || _active.Any(x => ReferenceEquals(x.Item, item)))
        {
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
            view = _selector.CreateView(item);
        }

        ApplyContext(view, item);
        view.Visibility = Visibility.Visible;
        if (!_host.Children.Contains(view))
        {
            _host.Children.Add(view);
        }

        _active.Add(new ViewItem { Item = item, View = view });
    }

    private void RemoveItem(object? item)
    {
        if (item is null)
        {
            return;
        }

        var index = _active.FindIndex(x => ReferenceEquals(x.Item, item));
        if (index < 0)
        {
            return;
        }

        var view = _active[index].View;
        view.Visibility = Visibility.Collapsed;
        view.DataContext = null;

        var itemType = item.GetType();
        if (!_pool.TryGetValue(itemType, out var pool))
        {
            pool = new Queue<FrameworkElement>();
            _pool[itemType] = pool;
        }

        pool.Enqueue(view);
        _active.RemoveAt(index);
    }

    private void ClearAll()
    {
        foreach (var item in _active)
        {
            item.View.Visibility = Visibility.Collapsed;
            item.View.DataContext = null;

            var itemType = item.Item.GetType();
            if (!_pool.TryGetValue(itemType, out var pool))
            {
                pool = new Queue<FrameworkElement>();
                _pool[itemType] = pool;
            }

            pool.Enqueue(item.View);
        }

        _active.Clear();
    }

    private static void ApplyContext(FrameworkElement view, object item)
    {
        view.DataContext = item;
    }

    private sealed class ViewItem
    {
        public object Item { get; set; } = null!;
        public FrameworkElement View { get; set; } = null!;
    }
}
