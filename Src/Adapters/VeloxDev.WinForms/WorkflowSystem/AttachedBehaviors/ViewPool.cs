using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
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
/// When <see cref="NodeLayerNameProperty"/> or <see cref="LinkLayerNameProperty"/> names a descendant element, views
/// are parented into that layer instead of the host itself: a view whose <see cref="VisualPanel.GetZIndex"/> is
/// negative goes to the link layer, every other view to the node layer. A missing or empty name keeps the view on the
/// host.
/// </para>
/// <para>
/// On a live surface, additions are queued and materialized in small batches on the UI thread, so panning does not
/// create every newly visible view on the input frame. Without a synchronization context or a created handle the
/// pool materializes synchronously, which is the headless test path.
/// </para>
/// </remarks>
public sealed class ViewPool
{
    private static readonly ConditionalWeakTable<VisualElement, PoolState> States = new();

    /// <summary>Raised after the pool adds, removes or reorders views on a host.</summary>
    /// <remarks>
    /// A surface subscribes once so attached behaviors on newly materialized views can be ensured; the pool
    /// itself stays workflow-free. It fires once per materialized view, not once per batch.
    /// </remarks>
    public static event EventHandler<ViewPoolChangedEventArgs>? Changed;

    /// <summary>The observable item source, normally <c>Helper.VisibleItems</c>.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource", typeof(object), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>Whether new items may be materialized in deferred UI batches instead of on the change event.</summary>
    /// <remarks>Off by default; enable only on a live WinForms message loop. Headless hosts stay synchronous.</remarks>
    public static readonly DependencyProperty DeferMaterializationProperty = DependencyProperty.RegisterAttached(
        "DeferMaterialization", typeof(bool), typeof(ViewPool),
        new PropertyMetadata(false));

    /// <summary>The selector that creates one visual per item.</summary>
    public static readonly DependencyProperty TemplateSelectorProperty = DependencyProperty.RegisterAttached(
        "TemplateSelector", typeof(VisualTemplateSelector), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>A single template used when no selector is set.</summary>
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.RegisterAttached(
        "ItemTemplate", typeof(DataTemplate), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>The name of the descendant element that receives node views.</summary>
    /// <remarks>
    /// Views whose <see cref="VisualPanel.GetZIndex"/> is not negative are parented into this element. When the name
    /// is empty or resolves to no element, node views stay on the host canvas.
    /// </remarks>
    public static readonly DependencyProperty NodeLayerNameProperty = DependencyProperty.RegisterAttached(
        "NodeLayerName", typeof(string), typeof(ViewPool),
        new PropertyMetadata(null));

    /// <summary>The name of the descendant element that receives link views.</summary>
    /// <remarks>
    /// Views whose <see cref="VisualPanel.GetZIndex"/> is negative are parented into this element. When the name is
    /// empty or resolves to no element, link views stay on the host canvas.
    /// </remarks>
    public static readonly DependencyProperty LinkLayerNameProperty = DependencyProperty.RegisterAttached(
        "LinkLayerName", typeof(string), typeof(ViewPool),
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

    /// <summary>Reads the deferred-materialization switch.</summary>
    public static bool GetDeferMaterialization(VisualElement element)
        => (bool)element.GetValue(DeferMaterializationProperty)!;

    /// <summary>Writes the deferred-materialization switch.</summary>
    public static void SetDeferMaterialization(VisualElement element, bool value)
        => element.SetValue(DeferMaterializationProperty, value);

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

    /// <summary>Reads the node layer name.</summary>
    public static string? GetNodeLayerName(VisualElement element)
        => (string?)element.GetValue(NodeLayerNameProperty);

    /// <summary>Writes the node layer name and re-creates the pool's views.</summary>
    public static void SetNodeLayerName(VisualElement element, string? value)
    {
        element.SetValue(NodeLayerNameProperty, value);
    }

    /// <summary>Reads the link layer name.</summary>
    public static string? GetLinkLayerName(VisualElement element)
        => (string?)element.GetValue(LinkLayerNameProperty);

    /// <summary>Writes the link layer name and re-creates the pool's views.</summary>
    public static void SetLinkLayerName(VisualElement element, string? value)
    {
        element.SetValue(LinkLayerNameProperty, value);
    }

    /// <summary>Gets the number of items that currently have a materialized view on <paramref name="host"/>.</summary>
    /// <param name="host">The pool host.</param>
    public static int GetActiveViewCount(VisualElement host)
        => States.TryGetValue(host ?? throw new ArgumentNullException(nameof(host)), out var state)
            ? state.ActiveViewCount
            : 0;

    /// <summary>Gets the number of items queued for a deferred materialization on <paramref name="host"/>.</summary>
    /// <param name="host">The pool host.</param>
    public static int GetPendingCount(VisualElement host)
        => States.TryGetValue(host ?? throw new ArgumentNullException(nameof(host)), out var state)
            ? state.PendingCount
            : 0;

    /// <summary>Materializes every item still waiting in the deferred queue, synchronously.</summary>
    /// <param name="host">The pool host.</param>
    /// <remarks>
    /// Any callback already posted to a synchronization context is invalidated, so pumping that context afterwards
    /// does not create a second view for the same item. This is the deterministic test and teardown path.
    /// </remarks>
    public static void Flush(VisualElement host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));

        if (States.TryGetValue(host, out var state))
        {
            state.FlushPending();
        }
    }

    private sealed class PoolState
    {
        private const int MaxBatchSize = 16;
        private const double BatchBudgetMilliseconds = 8d;
        private const int MaxRecycledPerType = 512;

        private readonly Dictionary<Type, Stack<VisualElement>> recycled = [];
        private readonly Queue<PendingEntry> pending = new();
        private readonly HashSet<object> pendingSet = new(ReferenceComparer.Instance);

        private bool scheduled;
        private int generation;

        public PoolState(VisualElement host)
        {
            Host = host;
        }

        public VisualElement Host { get; }

        public INotifyCollectionChanged? Source { get; set; }

        public IEnumerable? SourceItems { get; set; }

        public VisualTemplateSelector? Selector { get; set; }

        public DataTemplate? ItemTemplate { get; set; }

        public string? NodeLayerName { get; set; }

        public string? LinkLayerName { get; set; }

        public NotifyCollectionChangedEventHandler? Handler { get; set; }

        public Dictionary<object, VisualElement> Views { get; } = new(ReferenceComparer.Instance);

        public List<object> Order { get; } = [];

        public bool Syncing { get; set; }

        public int ActiveViewCount => Views.Count;

        public int PendingCount => pendingSet.Count;

        // 宿主已被释放时投递的回调不能再碰视觉树；没有宿主（无头测试）不算释放。
        private bool HostDisposed => Host.Surface is { IsDisposed: true };

        // 有消息泵才延迟建视图：宿主句柄已建，或当前线程带同步上下文（UI 线程/测试注入的上下文）。
        public bool CanDefer
        {
            get
            {
                var surface = Host.Surface;
                if (surface is { IsDisposed: true })
                {
                    return false;
                }

                if (surface is null)
                {
                    return SynchronizationContext.Current is not null;
                }

                // 只有宿主显式打开、且真的跑在 WinForms 消息循环里才分批；否则同步。
                return GetDeferMaterialization(Host)
                       && surface is { IsHandleCreated: true }
                       && Application.MessageLoop;
            }
        }

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
            if (Views.ContainsKey(item) || pendingSet.Contains(item))
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
            Order.Insert(ClampIndex(index), item);

            // 先按 ZIndex 选层再挂父级：链接模板用 -1 落在链接层，节点视图（>=0）落在节点层。
            var layer = ResolveLayer(view);
            layer.Add(view);
            // Set the data context after parenting so ElementName / ancestor-based bindings can resolve.
            view.DataContext = item;
            // 层可能是 CacheAsBitmap：子项变了，下一帧必须重新出一张位图。
            layer.InvalidateCache();
            Changed?.Invoke(null, new ViewPoolChangedEventArgs(Host));
        }

        public void Remove(object item)
        {
            if (!Views.TryGetValue(item, out var view))
            {
                // 待建项还没进 Views：从待建集合摘掉即可，队列里的残留由批次处理跳过。
                pendingSet.Remove(item);
                return;
            }

            Views.Remove(item);

            Order.Remove(item);
            view.PropertyChanged -= OnViewPropertyChanged;
            if (view.ParentElement is { } parent)
            {
                parent.RemoveChild(view);
                parent.InvalidateCache();
            }

            view.DataContext = null;
            Recycle(item.GetType(), view);
        }

        public void Clear(bool disposeAll = false)
        {
            DiscardPending();

            foreach (var pair in Views.ToArray())
            {
                var view = pair.Value;
                view.PropertyChanged -= OnViewPropertyChanged;
                if (view.ParentElement is { } parent)
                {
                    parent.RemoveChild(view);
                    parent.InvalidateCache();
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

        public bool Enqueue(object item, int index)
        {
            if (Views.ContainsKey(item) || !pendingSet.Add(item))
            {
                return false;
            }

            pending.Enqueue(new PendingEntry(item, index));
            return true;
        }

        public void ScheduleNextBatch()
        {
            if (scheduled || pendingSet.Count == 0 || HostDisposed)
            {
                return;
            }

            var captured = generation;

            if (SynchronizationContext.Current is { } context)
            {
                scheduled = true;
                try
                {
                    // 批次回调只认捕获时的代次：Refresh / Flush 之后到达的旧回调直接作废。
                    context.Post(_ => RunScheduled(captured), null);
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // 上下文在投递前失效：落到宿主消息泵或同步路径。
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.InvalidAsynchronousStateException)
                {
                }

                scheduled = false;
            }

            if (Host.Surface is { IsHandleCreated: true, IsDisposed: false } surface)
            {
                scheduled = true;
                try
                {
                    surface.BeginInvoke((Action)(() => RunScheduled(captured)));
                    return;
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.InvalidAsynchronousStateException)
                {
                }

                scheduled = false;
            }

            // 没有可用的消息泵：同步完成，和无 UI 上下文时一致。
            FlushPending();
        }

        public void FlushPending()
        {
            scheduled = false;
            // 作废已投递但尚未执行的回调，避免同一项被建两次。
            generation++;

            if (HostDisposed)
            {
                pending.Clear();
                pendingSet.Clear();
                return;
            }

            if (pendingSet.Count == 0)
            {
                pending.Clear();
                return;
            }

            var materialized = false;
            while (pending.Count > 0)
            {
                var entry = pending.Dequeue();
                if (!pendingSet.Remove(entry.Item) || Views.ContainsKey(entry.Item))
                {
                    continue;
                }

                Add(entry.Item, entry.Index);
                materialized = true;
            }

            pending.Clear();
            if (materialized)
            {
                Reorder();
            }
        }

        private void RunScheduled(int captured)
        {
            if (captured != generation || HostDisposed)
            {
                return;
            }

            ProcessBatch();
        }

        private void ProcessBatch()
        {
            scheduled = false;
            if (HostDisposed)
            {
                pending.Clear();
                pendingSet.Clear();
                return;
            }

            var started = Stopwatch.GetTimestamp();
            var budgetTicks = (long)(BatchBudgetMilliseconds * Stopwatch.Frequency / 1000d);
            var materialized = false;
            var processed = 0;

            while (processed < MaxBatchSize && pendingSet.Count > 0 && pending.Count > 0)
            {
                var entry = pending.Dequeue();
                // 已移除的待建项是墓碑：从待建集合摘除，这里跳过即可。
                if (!pendingSet.Remove(entry.Item) || Views.ContainsKey(entry.Item))
                {
                    continue;
                }

                Add(entry.Item, entry.Index);
                materialized = true;
                processed++;

                if (Stopwatch.GetTimestamp() - started >= budgetTicks)
                {
                    break;
                }
            }

            if (pendingSet.Count == 0)
            {
                pending.Clear();
            }

            if (materialized)
            {
                Reorder();
            }

            if (pendingSet.Count > 0)
            {
                ScheduleNextBatch();
            }
        }

        private void DiscardPending()
        {
            generation++;
            scheduled = false;
            pending.Clear();
            pendingSet.Clear();
        }

        private int ClampIndex(int index)
            => index < 0 ? 0 : index > Order.Count ? Order.Count : index;

        private VisualElement ResolveLayer(VisualElement view)
        {
            var name = VisualPanel.GetZIndex(view) < 0 ? LinkLayerName : NodeLayerName;
            return FindLayer(Host, name) ?? Host;
        }

        private static VisualElement? FindLayer(VisualElement parent, string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (var child in parent.Children)
            {
                if (child is not VisualElement element)
                {
                    continue;
                }

                if (string.Equals(element.Name, name, StringComparison.Ordinal))
                {
                    return element;
                }

                if (FindLayer(element, name) is { } nested)
                {
                    return nested;
                }
            }

            return null;
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
                Dictionary<VisualElement, List<(VisualElement View, int Index)>>? groups = null;
                for (var index = 0; index < Order.Count; index++)
                {
                    if (!Views.TryGetValue(Order[index], out var view))
                    {
                        continue;
                    }

                    var layer = view.ParentElement ?? Host;
                    groups ??= [];
                    if (!groups.TryGetValue(layer, out var entries))
                    {
                        entries = [];
                        groups[layer] = entries;
                    }

                    entries.Add((view, index));
                }

                if (groups is null)
                {
                    return;
                }

                foreach (var pair in groups)
                {
                    ReorderLayer(pair.Key, pair.Value);
                }
            }
            finally
            {
                Syncing = false;
            }
        }

        // 只在层内部调整池视图：层画布本身和别的子项都留在原槽位。
        private static void ReorderLayer(VisualElement layer, List<(VisualElement View, int Index)> entries)
        {
            var ordered = entries
                .OrderBy(static entry => VisualPanel.GetZIndex(entry.View))
                .ThenBy(static entry => entry.Index)
                .Select(static entry => (Visual)entry.View)
                .ToArray();

            var members = new HashSet<VisualElement>();
            foreach (var entry in entries)
            {
                members.Add(entry.View);
            }

            var children = layer.Children;
            var slots = new List<int>(ordered.Length);
            for (var index = 0; index < children.Count; index++)
            {
                if (children[index] is VisualElement element && members.Contains(element))
                {
                    slots.Add(index);
                }
            }

            if (slots.Count != ordered.Length)
            {
                return;
            }

            var changed = false;
            for (var index = 0; index < ordered.Length && !changed; index++)
            {
                changed = !ReferenceEquals(children[slots[index]], ordered[index]);
            }

            if (!changed)
            {
                return;
            }

            // 先摘后插：槽位集合保持不变，非池子项的位置也不动。
            for (var index = slots.Count - 1; index >= 0; index--)
            {
                children.RemoveAt(slots[index]);
            }

            for (var index = 0; index < ordered.Length; index++)
            {
                children.Insert(Math.Min(slots[index], children.Count), ordered[index]);
            }

            // 顺序即绘制顺序；缓存层必须重出图才会反映新的先后。
            layer.InvalidateCache();
        }

        private void OnViewPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.Property == VisualPanel.ZIndexProperty)
            {
                Reorder();
            }
        }

        private readonly record struct PendingEntry(object Item, int Index);
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
                || e.Property == ItemTemplateProperty
                || e.Property == NodeLayerNameProperty
                || e.Property == LinkLayerNameProperty
                || e.Property == DeferMaterializationProperty))
        {
            Refresh(host);
        }
    }

    /// <summary>Re-creates the views for the current source and configuration.</summary>
    /// <param name="host">The pool host.</param>
    /// <remarks>
    /// Pending deferred work is discarded first: the rebuild enumerates the whole source synchronously, so the views
    /// that were waiting are re-created here rather than by a stale callback.
    /// </remarks>
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
        state.NodeLayerName = GetNodeLayerName(host);
        state.LinkLayerName = GetLinkLayerName(host);
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
                if (state.CanDefer)
                {
                    // 把这一帧要新增的视图排进队列，最多 16 个或 8ms 后再建下一批。
                    var insert = e.NewStartingIndex < 0
                        ? state.Order.Count + state.PendingCount
                        : e.NewStartingIndex;
                    var enqueued = false;
                    foreach (var item in e.NewItems)
                    {
                        if (item is not null)
                        {
                            enqueued |= state.Enqueue(item, insert++);
                        }
                    }

                    if (enqueued)
                    {
                        state.ScheduleNextBatch();
                    }
                }
                else
                {
                    var insert = e.NewStartingIndex < 0 ? state.Order.Count : e.NewStartingIndex;
                    foreach (var item in e.NewItems)
                    {
                        if (item is not null)
                        {
                            state.Add(item, insert++);
                        }
                    }

                    state.Reorder();
                }

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
