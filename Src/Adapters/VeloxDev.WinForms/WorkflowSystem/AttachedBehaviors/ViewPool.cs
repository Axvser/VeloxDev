using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VisualPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.Panel;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A workflow-free visual view pool: keeps one visual child per item in an observable source, choosing the view
/// through a <see cref="VisualTemplateSelector"/> or a single <see cref="DataTemplate"/>.
/// </summary>
/// <remarks>
/// <para>
/// Attach it to a visual <see cref="Canvas"/> and bind <see cref="ItemsSourceProperty"/> to the virtualized item
/// collection (normally <c>Helper.VisibleItems</c>). Each created view receives the item as its
/// <see cref="VisualElement.DataContext"/>; the template owns all positioning and painting.
/// </para>
/// <para>
/// Children are ordered by <see cref="VisualPanel.GetZIndex"/> and then by source order, so links can sit below cards
/// through <c>Panel.ZIndex="-1"</c> without the pool knowing what a link is.
/// </para>
/// </remarks>
public sealed class ViewPool
{
    private static readonly ConditionalWeakTable<VisualElement, PoolState> States = new();

    /// <summary>Raised after the pool adds, removes or reorders views on a host.</summary>
    /// <remarks>
    /// A surface subscribes once so attached behaviors on newly materialized views can be ensured; the pool
    /// itself stays workflow-free.
    /// </remarks>
    public static event EventHandler<ViewPoolChangedEventArgs>? Changed;

    /// <summary>The observable item source, normally <c>Helper.VisibleItems</c>.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource", typeof(object), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>The selector that creates one visual per item.</summary>
    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector", typeof(VisualTemplateSelector), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>A single template used when no selector is set.</summary>
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.RegisterAttached(
        "ItemTemplate", typeof(DataTemplate), typeof(ViewPool),
        new PropertyMetadata(null));

    private ViewPool()
    {
    }

    /// <summary>Reads the item source.</summary>
    public static object? GetItemsSource(VisualElement element) => element.GetValue(ItemsSourceProperty);

    /// <summary>Writes the item source.</summary>
    public static void SetItemsSource(VisualElement element, object? value)
    {
        element.SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Reads the selector.</summary>
    public static VisualTemplateSelector? GetTemplateSelector(VisualElement element)
        => (VisualTemplateSelector?)element.GetValue(TemplateSelectorProperty);

    /// <summary>Writes the selector.</summary>
    public static void SetTemplateSelector(VisualElement element, VisualTemplateSelector? value)
    {
        element.SetValue(TemplateSelectorProperty, value);
    }

    /// <summary>Reads the single template.</summary>
    public static DataTemplate? GetItemTemplate(VisualElement element)
        => (DataTemplate?)element.GetValue(ItemTemplateProperty);

    /// <summary>Writes the single template.</summary>
    public static void SetItemTemplate(VisualElement element, DataTemplate? value)
    {
        element.SetValue(ItemTemplateProperty, value);
    }

    private sealed class PoolState
    {
        public PoolState(VisualElement host)
        {
            Host = host;
        }

        public VisualElement Host { get; }

        public INotifyCollectionChanged? Source { get; set; }

        public IEnumerable? SourceItems { get; set; }

        public VisualTemplateSelector? Selector { get; set; }

        public DataTemplate? ItemTemplate { get; set; }

        public NotifyCollectionChangedEventHandler? Handler { get; set; }

        public Dictionary<object, VisualElement> Views { get; } = new(ReferenceComparer.Instance);

        public List<object> Order { get; } = [];

        private const int MaxRecycledPerType = 512;

        private readonly Dictionary<Type, Stack<VisualElement>> recycled = [];

        public bool Syncing { get; set; }

        public void Rebuild(bool disposeAll = false)
        {
            Clear(disposeAll);
            if (SourceItems is null)
            {
                return;
            }

            foreach (var item in SourceItems)
            {
                if (item is not null)
                {
                    Add(item, Order.Count);
                }
            }

            Reorder();
        }

        public void Add(object item, int index)
        {
            if (Views.ContainsKey(item))
            {
                return;
            }

            VisualElement? view = null;
            if (recycled.TryGetValue(item.GetType(), out var cache) && cache.Count > 0)
            {
                view = cache.Pop();
            }

            view ??= Selector?.SelectTemplate(item) ?? ItemTemplate?.Create();
            if (view is null)
            {
                return;
            }

            view.PropertyChanged += OnViewPropertyChanged;
            Views[item] = view;
            Order.Insert(index < 0 ? 0 : index > Order.Count ? Order.Count : index, item);
            Host.Add(view);
            // Set the data context after parenting so ElementName / ancestor-based bindings can resolve.
            view.DataContext = item;
            Changed?.Invoke(null, new ViewPoolChangedEventArgs(Host));
        }

        public void Remove(object item)
        {
            if (!Views.TryGetValue(item, out var view))
            {
                return;
            }

            Views.Remove(item);

            Order.Remove(item);
            view.PropertyChanged -= OnViewPropertyChanged;
            if (ReferenceEquals(view.ParentElement, Host))
            {
                Host.RemoveChild(view);
            }

            view.DataContext = null;
            Recycle(item.GetType(), view);
        }

        public void Clear(bool disposeAll = false)
        {
            foreach (var pair in Views.ToArray())
            {
                var view = pair.Value;
                view.PropertyChanged -= OnViewPropertyChanged;
                if (ReferenceEquals(view.ParentElement, Host))
                {
                    Host.RemoveChild(view);
                }

                if (disposeAll)
                {
                    view.Dispose();
                }
                else
                {
                    view.DataContext = null;
                    Recycle(pair.Key.GetType(), view);
                }
            }

            Views.Clear();
            Order.Clear();
            if (disposeAll)
            {
                DisposeRecycled();
            }
        }

        private void Recycle(Type itemType, VisualElement view)
        {
            if (!recycled.TryGetValue(itemType, out var cache))
            {
                cache = [];
                recycled[itemType] = cache;
            }

            if (cache.Count < MaxRecycledPerType)
            {
                cache.Push(view);
                return;
            }

            view.Dispose();
        }

        private void DisposeRecycled()
        {
            foreach (var cache in recycled.Values)
            {
                foreach (var view in cache)
                {
                    view.Dispose();
                }
            }

            recycled.Clear();
        }

        public void Reorder()
        {
            if (Syncing || Order.Count == 0)
            {
                return;
            }

            Syncing = true;
            try
            {
                var ordered = Order
                    .Select((item, index) => (View: Views[item], Index: index))
                    .OrderBy(static pair => VisualPanel.GetZIndex(pair.View))
                    .ThenBy(static pair => pair.Index)
                    .Select(static pair => pair.View)
                    .ToArray();

                for (var index = 0; index < ordered.Length; index++)
                {
                    var view = ordered[index];
                    var current = Host.Children.IndexOf(view);
                    if (current == index)
                    {
                        continue;
                    }

                    if (current >= 0)
                    {
                        Host.Children.RemoveAt(current);
                    }

                    if (index >= Host.Children.Count)
                    {
                        Host.Children.Add(view);
                    }
                    else
                    {
                        Host.Children.Insert(index, view);
                    }
                }
            }
            finally
            {
                Syncing = false;
            }
        }

        private void OnViewPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.Property == VisualPanel.ZIndexProperty)
            {
                Reorder();
            }
        }
    }

    private static PoolState GetState(VisualElement host)
        => States.GetValue(host, static element =>
        {
            var state = new PoolState(element);
            element.PropertyChanged += OnHostPropertyChanged;
            return state;
        });

    private static void OnHostPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is VisualElement host
            && (e.Property == ItemsSourceProperty
                || e.Property == TemplateSelectorProperty
                || e.Property == ItemTemplateProperty))
        {
            Refresh(host);
        }
    }

    /// <summary>Re-creates the views for the current source and configuration.</summary>
    public static void Refresh(VisualElement host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));

        var state = GetState(host);

        state.Handler ??= (_, e) => Apply(host, e);
        if (state.Source is not null)
        {
            state.Source.CollectionChanged -= state.Handler;
        }

        state.Selector = GetTemplateSelector(host);
        state.ItemTemplate = GetItemTemplate(host);
        state.Source = GetItemsSource(host) as INotifyCollectionChanged;
        state.SourceItems = GetItemsSource(host) as IEnumerable;
        state.Rebuild(disposeAll: true);
        if (state.Source is not null)
        {
            state.Source.CollectionChanged += state.Handler;
        }
    }

    private static void Apply(VisualElement host, NotifyCollectionChangedEventArgs e)
    {
        if (!States.TryGetValue(host, out var state) || state.Syncing)
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                var insert = e.NewStartingIndex < 0 ? state.Order.Count : e.NewStartingIndex;
                foreach (var item in e.NewItems)
                {
                    if (item is not null)
                    {
                        state.Add(item, insert++);
                    }
                }

                state.Reorder();
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (var item in e.OldItems)
                {
                    if (item is not null)
                    {
                        state.Remove(item);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Replace when e.OldItems is not null && e.NewItems is not null:
                for (var index = 0; index < Math.Min(e.OldItems.Count, e.NewItems.Count); index++)
                {
                    if (e.OldItems[index] is { } oldItem && e.NewItems[index] is { } newItem)
                    {
                        state.Remove(oldItem);
                        state.Add(newItem, index);
                    }
                }

                state.Reorder();
                break;

            case NotifyCollectionChangedAction.Reset:
                state.Rebuild();
                break;
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>Identifies the visual Canvas whose pooled views changed.</summary>
public sealed class ViewPoolChangedEventArgs(VisualElement host) : EventArgs
{
    /// <summary>The Canvas that owns the pool.</summary>
    public VisualElement Host { get; } = host;
}
