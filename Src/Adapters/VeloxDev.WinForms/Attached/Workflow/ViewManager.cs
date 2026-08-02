using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Resolves a host control (view) for a workflow item, mirroring the role of a
/// <c>DataTemplateSelector</c> in the XAML adapters.
/// </summary>
public interface IWorkflowTemplateSelector
{
    /// <summary>
    /// Creates a new view control for the specified workflow item.
    /// </summary>
    Control CreateView(object item);
}

/// <summary>
/// WinForms view pooling manager. Attaches to a collection and materializes one
/// view control per item into a host control, reusing pooled instances by item type
/// and hiding them again when items are removed. This mirrors the WPF/Avalonia/WinUI/MAUI
/// <c>ViewManager</c> + <c>ViewPool</c> pairing.
/// </summary>
public sealed class ViewManager : IDisposable
{
    private readonly Control _host;
    private readonly Dictionary<Type, Queue<Control>> _pool = [];
    private readonly List<ControlItem> _active = [];
    private INotifyCollectionChanged? _collection;
    private IWorkflowTemplateSelector? _selector;

    /// <summary>
    /// Initializes a view manager that renders pooled item views into the specified host control.
    /// </summary>
    public ViewManager(Control host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Sets the template selector used to materialize item views. Must be set before
    /// <see cref="Attach"/> for items to render.
    /// </summary>
    public void SetTemplateSelector(IWorkflowTemplateSelector selector)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    /// <summary>
    /// Binds the manager to a collection, replacing any previous binding.
    /// </summary>
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

    /// <summary>
    /// Detaches from the collection and hides all pooled views.
    /// </summary>
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
        Control view;

        if (_pool.TryGetValue(itemType, out var pool) && pool.Count > 0)
        {
            view = pool.Dequeue();
        }
        else
        {
            view = _selector.CreateView(item);
        }

        ApplyContext(view, item);
        view.Visible = true;
        if (!_host.Controls.Contains(view))
        {
            _host.Controls.Add(view);
        }

        view.BringToFront();
        _active.Add(new ControlItem { Item = item, View = view });
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
        view.Visible = false;
        view.Tag = null;

        var itemType = item.GetType();
        if (!_pool.TryGetValue(itemType, out var pool))
        {
            pool = new Queue<Control>();
            _pool[itemType] = pool;
        }

        pool.Enqueue(view);
        _active.RemoveAt(index);
    }

    private void ClearAll()
    {
        foreach (var item in _active)
        {
            item.View.Visible = false;
            item.View.Tag = null;

            var itemType = item.Item.GetType();
            if (!_pool.TryGetValue(itemType, out var pool))
            {
                pool = new Queue<Control>();
                _pool[itemType] = pool;
            }

            pool.Enqueue(item.View);
        }

        _active.Clear();
    }

    private static void ApplyContext(Control view, object item)
    {
        // WinForms has no DataContext; honor the common "Tag as context" convention
        // used throughout the VeloxDev WinForms demos, plus optional ViewModel/DataContext
        // properties so control designers can bind explicitly.
        view.Tag = item;

        foreach (var propertyName in new[] { "ViewModel", "DataContext", "BindingContext" })
        {
            var property = view.GetType().GetProperty(propertyName);
            if (property is not null && property.CanWrite && property.PropertyType.IsInstanceOfType(item))
            {
                try
                {
                    property.SetValue(view, item);
                }
                catch
                {
                    // Best-effort context wiring; the view may require designer setup.
                }
            }
        }
    }

    private sealed class ControlItem
    {
        public object Item { get; set; } = null!;
        public Control View { get; set; } = null!;
    }
}
