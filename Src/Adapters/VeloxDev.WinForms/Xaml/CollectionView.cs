using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VeloxDev.WinForms.Xaml;

/// <summary>One property name and direction used to sort a <see cref="CollectionView"/>.</summary>
/// <remarks>
/// The property name is a dot-separated path resolved with the same rules as a binding path, so
/// <c>Owner.Name</c> and <c>Items[0].Name</c> both work. An empty name sorts by the item itself.
/// </remarks>
public readonly struct SortDescription : IEquatable<SortDescription>
{
    /// <summary>Creates an ascending sort on <paramref name="propertyName"/>.</summary>
    /// <param name="propertyName">The dot-separated property path to sort by.</param>
    public SortDescription(string propertyName)
        : this(propertyName, ListSortDirection.Ascending)
    {
    }

    /// <summary>Creates a sort on <paramref name="propertyName"/> in the given direction.</summary>
    /// <param name="propertyName">The dot-separated property path to sort by.</param>
    /// <param name="direction">Whether the smallest or the largest value comes first.</param>
    public SortDescription(string propertyName, ListSortDirection direction)
    {
        PropertyName = propertyName ?? string.Empty;
        Direction = direction;
    }

    /// <summary>The dot-separated property path to sort by; empty sorts by the item itself.</summary>
    public string PropertyName { get; }

    /// <summary>The sort direction.</summary>
    public ListSortDirection Direction { get; }

    /// <inheritdoc />
    public bool Equals(SortDescription other)
        => string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal) && Direction == other.Direction;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SortDescription other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => (StringComparer.Ordinal.GetHashCode(PropertyName) * 397) ^ (int)Direction;

    /// <summary>Compares two descriptions by property name and direction.</summary>
    /// <param name="left">The first description.</param>
    /// <param name="right">The second description.</param>
    public static bool operator ==(SortDescription left, SortDescription right) => left.Equals(right);

    /// <summary>Compares two descriptions by property name and direction.</summary>
    /// <param name="left">The first description.</param>
    /// <param name="right">The second description.</param>
    public static bool operator !=(SortDescription left, SortDescription right) => !left.Equals(right);
}

/// <summary>Names one property path whose values split the view into groups.</summary>
/// <remarks>
/// Grouping is shallow and value-based: items with equal path values form one group, ordered by first appearance.
/// Changing <see cref="PropertyName"/> on a description already inside a group collection does not notify the view;
/// call <see cref="CollectionView.Refresh"/> afterwards.
/// </remarks>
public sealed class GroupDescription
{
    /// <summary>Creates a group description with an empty property name.</summary>
    public GroupDescription()
    {
    }

    /// <summary>Creates a group description over <paramref name="propertyName"/>.</summary>
    /// <param name="propertyName">The dot-separated property path used as the group key.</param>
    public GroupDescription(string propertyName)
    {
        PropertyName = propertyName ?? string.Empty;
    }

    /// <summary>The dot-separated property path used as the group key; empty groups by the item itself.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string ToString() => PropertyName;
}

/// <summary>One group produced by a <see cref="CollectionView"/> for a <see cref="GroupDescription"/>.</summary>
public sealed class CollectionViewGroup
{
    internal CollectionViewGroup(
        object? name,
        IReadOnlyList<object?> items,
        IReadOnlyList<CollectionViewGroup> subgroups)
    {
        Name = name;
        Items = items;
        Subgroups = subgroups;
    }

    /// <summary>The value shared by every member of this group.</summary>
    public object? Name { get; }

    /// <summary>The members of this group; empty when <see cref="Subgroups"/> carries the leaves.</summary>
    public IReadOnlyList<object?> Items { get; }

    /// <summary>The next grouping level, or empty when this is the leaf level.</summary>
    public IReadOnlyList<CollectionViewGroup> Subgroups { get; }

    /// <summary>The number of direct members.</summary>
    public int ItemCount => Items.Count;
}

/// <summary>The event data raised before a <see cref="CollectionView"/> moves its current item.</summary>
public sealed class CurrentChangingEventArgs : CancelEventArgs
{
    /// <summary>Creates arguments that a handler may cancel.</summary>
    public CurrentChangingEventArgs()
    {
        CanCancel = true;
    }

    /// <summary>Creates arguments and states whether a handler may cancel the move.</summary>
    /// <param name="canCancel">Whether setting <see cref="CancelEventArgs.Cancel"/> stops the move.</param>
    public CurrentChangingEventArgs(bool canCancel)
    {
        CanCancel = canCancel;
    }

    /// <summary>Whether setting <see cref="CancelEventArgs.Cancel"/> stops the move.</summary>
    public bool CanCancel { get; }
}

/// <summary>
/// A filtered, sorted and grouped view over an <see cref="IEnumerable"/>, with a current item.
/// </summary>
/// <remarks>
/// <para>
/// The view is a materialized snapshot: <see cref="Refresh"/> enumerates the source again, applies
/// <see cref="Filter"/>, orders by <see cref="SortDescriptions"/> and splits by <see cref="GroupDescriptions"/>.
/// Adding, removing or replacing a descriptor refreshes automatically.
/// </para>
/// <para>
/// This type is single-threaded and has no synchronization. Call it from the UI thread, and treat any
/// <see cref="INotifyCollectionChanged"/> notification from the source as arriving on that same thread;
/// the handler refreshes synchronously on the notifying thread.
/// </para>
/// <para>
/// The WinForms adapter does not reference WindowsBase, so this class mirrors the shape of the WPF
/// <c>System.ComponentModel.ICollectionView</c> contract instead of implementing that interface.
/// </para>
/// </remarks>
public sealed class CollectionView : IEnumerable<object?>, INotifyCollectionChanged, INotifyPropertyChanged, IDisposable
{
    private readonly IEnumerable _source;
    private readonly ObservableCollection<SortDescription> _sortDescriptions = [];
    private readonly ObservableCollection<GroupDescription> _groupDescriptions = [];
    private List<object?> _items = [];
    private List<CollectionViewGroup> _groups = [];
    private Predicate<object>? _filter;
    private int _currentPosition = -1;
    private bool _suspendRefresh;
    private bool _disposed;

    /// <summary>Creates a view over <paramref name="source"/>.</summary>
    /// <param name="source">The sequence the view reads; it may also raise <see cref="INotifyCollectionChanged"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public CollectionView(IEnumerable source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (source is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += OnSourceCollectionChanged;
        }

        _sortDescriptions.CollectionChanged += OnViewDescriptionChanged;
        _groupDescriptions.CollectionChanged += OnViewDescriptionChanged;
        Refresh();
    }

    /// <summary>Raised after the visible items are rebuilt; the action is always <see cref="NotifyCollectionChangedAction.Reset"/>.</summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Raised when a view property such as <see cref="Count"/>, <see cref="CurrentItem"/> or <see cref="Groups"/> changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the current position changes.</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>Raised before the current position changes; a handler may cancel the move.</summary>
    public event EventHandler<CurrentChangingEventArgs>? CurrentChanging;

    /// <summary>The sequence this view reads.</summary>
    public IEnumerable SourceCollection => _source;

    /// <summary>The sort keys applied in order; the first key is the primary key.</summary>
    public ObservableCollection<SortDescription> SortDescriptions => _sortDescriptions;

    /// <summary>The group keys applied from the outermost level inward.</summary>
    public ObservableCollection<GroupDescription> GroupDescriptions => _groupDescriptions;

    /// <summary>The filter applied before sorting and grouping; <see langword="null"/> keeps every item.</summary>
    public Predicate<object>? Filter
    {
        get => _filter;
        set
        {
            if (ReferenceEquals(_filter, value))
            {
                return;
            }

            _filter = value;
            Refresh();
        }
    }

    /// <summary>The groups produced by <see cref="GroupDescriptions"/>; empty when no grouping applies.</summary>
    public IReadOnlyList<CollectionViewGroup> Groups => _groups;

    /// <summary>The number of visible items after filtering.</summary>
    public int Count => _items.Count;

    /// <summary>Whether the view currently holds no item.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>The item at the current position, or <see langword="null"/> when the position is outside the view.</summary>
    public object? CurrentItem
        => _currentPosition >= 0 && _currentPosition < _items.Count ? _items[_currentPosition] : null;

    /// <summary>The current position; -1 is before the first item and <see cref="Count"/> is after the last.</summary>
    public int CurrentPosition => _currentPosition;

    /// <summary>Whether the current position is before the first item.</summary>
    public bool IsCurrentBeforeFirst => _currentPosition < 0;

    /// <summary>Whether the current position is at or after the last item.</summary>
    public bool IsCurrentAfterLast => _currentPosition >= _items.Count;

    /// <summary>Returns the visible item at <paramref name="index"/>.</summary>
    /// <param name="index">A zero-based index into the visible items.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the visible items.</exception>
    public object? this[int index] => _items[index];

    /// <summary>Returns the position of <paramref name="item"/> in the visible items, or -1 when it is absent.</summary>
    /// <param name="item">The item to locate; comparison uses <see cref="object.Equals(object, object)"/>.</param>
    public int IndexOf(object? item)
    {
        for (var index = 0; index < _items.Count; index++)
        {
            if (Equals(_items[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Whether <paramref name="item"/> survives the filter and is visible.</summary>
    /// <param name="item">The item to test.</param>
    public bool Contains(object? item) => IndexOf(item) >= 0;

    /// <summary>Re-enumerates the source and rebuilds the visible items, groups and current position.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var oldPosition = _currentPosition;
        var oldCount = _items.Count;
        var hadCurrent = oldPosition >= 0 && oldPosition < oldCount;
        var oldCurrent = hadCurrent ? _items[oldPosition] : null;
        var wasAfterLast = oldPosition >= oldCount && oldCount > 0;

        var filtered = new List<object?>();
        foreach (var item in _source)
        {
            if (_filter is null || _filter(item!))
            {
                filtered.Add(item);
            }
        }

        _items = _sortDescriptions.Count == 0 ? filtered : SortItems(filtered);
        RebuildGroups();

        var newPosition = hadCurrent ? IndexOf(oldCurrent) : wasAfterLast && _items.Count > 0 ? _items.Count : -1;
        _currentPosition = newPosition;

        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        RaisePropertyChanged(nameof(Count));
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(Groups));
        RaisePropertyChanged(nameof(CurrentItem));
        RaisePropertyChanged(nameof(CurrentPosition));
        if (oldPosition != newPosition)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Moves the current position to <paramref name="item"/>.</summary>
    /// <param name="item">An item in the visible set.</param>
    /// <returns><see langword="true"/> when the position ends on a visible item.</returns>
    public bool MoveCurrentTo(object? item)
    {
        var index = IndexOf(item);
        return index >= 0 && MoveCurrentToPosition(index);
    }

    /// <summary>Moves the current position to the first visible item.</summary>
    /// <returns><see langword="false"/> when the view is empty.</returns>
    public bool MoveCurrentToFirst() => _items.Count > 0 && MoveCurrentToPosition(0);

    /// <summary>Moves the current position to the last visible item.</summary>
    /// <returns><see langword="false"/> when the view is empty.</returns>
    public bool MoveCurrentToLast() => _items.Count > 0 && MoveCurrentToPosition(_items.Count - 1);

    /// <summary>Moves the current position one item forward.</summary>
    /// <returns><see langword="false"/> when the new position is past the last item.</returns>
    public bool MoveCurrentToNext()
        => _items.Count > 0 && MoveCurrentToPosition(Math.Min(_currentPosition + 1, _items.Count));

    /// <summary>Moves the current position one item backward.</summary>
    /// <returns><see langword="false"/> when the new position is before the first item.</returns>
    public bool MoveCurrentToPrevious()
        => _items.Count > 0 && MoveCurrentToPosition(_currentPosition <= 0 ? -1 : _currentPosition - 1);

    /// <summary>Moves the current position to <paramref name="position"/>.</summary>
    /// <param name="position">An index into the visible items; -1 places the position before the first item.</param>
    /// <returns><see langword="true"/> when the position ends on a visible item.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> is less than -1 or greater than <see cref="Count"/>.
    /// </exception>
    public bool MoveCurrentToPosition(int position)
    {
        if (position < -1 || position > _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (position == _currentPosition)
        {
            return position >= 0 && position < _items.Count;
        }

        if (!RaiseCurrentChanging())
        {
            return false;
        }

        _currentPosition = position;
        RaisePropertyChanged(nameof(CurrentItem));
        RaisePropertyChanged(nameof(CurrentPosition));
        RaisePropertyChanged(nameof(IsCurrentBeforeFirst));
        RaisePropertyChanged(nameof(IsCurrentAfterLast));
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return position >= 0 && position < _items.Count;
    }

    /// <summary>Returns an enumerator over the visible items.</summary>
    public IEnumerator<object?> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Detaches from the source collection and stops raising events.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_source is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged -= OnSourceCollectionChanged;
        }

        _sortDescriptions.CollectionChanged -= OnViewDescriptionChanged;
        _groupDescriptions.CollectionChanged -= OnViewDescriptionChanged;
    }

    // CollectionViewSource 构造新视图时逐个复制描述；复制过程不能触发一轮轮刷新。
    internal void ApplyViewSettings(
        IEnumerable<SortDescription> sorts,
        Predicate<object>? filter,
        IEnumerable<GroupDescription> groups)
    {
        _suspendRefresh = true;
        try
        {
            _filter = filter;
            _sortDescriptions.Clear();
            foreach (var sort in sorts)
            {
                _sortDescriptions.Add(sort);
            }

            _groupDescriptions.Clear();
            foreach (var group in groups)
            {
                _groupDescriptions.Add(group);
            }
        }
        finally
        {
            _suspendRefresh = false;
        }

        Refresh();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnViewDescriptionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_suspendRefresh)
        {
            Refresh();
        }
    }

    private bool RaiseCurrentChanging()
    {
        var handler = CurrentChanging;
        if (handler is null)
        {
            return true;
        }

        var args = new CurrentChangingEventArgs();
        handler(this, args);
        return !args.Cancel;
    }

    private void RaisePropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private List<object?> SortItems(List<object?> items)
    {
        IEnumerable<object?> query = items;

        // OrderBy 是稳定排序：从最后一个键往前套，等于按键的优先级从高到低排序。
        for (var index = _sortDescriptions.Count - 1; index >= 0; index--)
        {
            var description = _sortDescriptions[index];
            var path = new BindingPath(description.PropertyName);
            query = description.Direction == ListSortDirection.Descending
                ? query.OrderByDescending(item => ResolveKey(path, item), ValueComparer.Instance)
                : query.OrderBy(item => ResolveKey(path, item), ValueComparer.Instance);
        }

        return query.ToList();
    }

    private void RebuildGroups()
    {
        _groups = [];
        if (_groupDescriptions.Count == 0)
        {
            return;
        }

        _groups = BuildGroups(_items, 0);
    }

    private List<CollectionViewGroup> BuildGroups(IEnumerable<object?> items, int depth)
    {
        var description = _groupDescriptions[depth];
        var path = new BindingPath(description.PropertyName);
        var keys = new List<object?>();
        var buckets = new List<List<object?>>();

        foreach (var item in items)
        {
            var key = ResolveKey(path, item);
            var index = FindKey(keys, key);
            if (index < 0)
            {
                index = keys.Count;
                keys.Add(key);
                buckets.Add([]);
            }

            buckets[index].Add(item);
        }

        var groups = new List<CollectionViewGroup>(keys.Count);
        for (var index = 0; index < keys.Count; index++)
        {
            var members = buckets[index];
            groups.Add(depth + 1 < _groupDescriptions.Count
                ? new CollectionViewGroup(keys[index], Array.Empty<object?>(), BuildGroups(members, depth + 1))
                : new CollectionViewGroup(keys[index], members, Array.Empty<CollectionViewGroup>()));
        }

        return groups;
    }

    private static int FindKey(List<object?> keys, object? key)
    {
        for (var index = 0; index < keys.Count; index++)
        {
            if (Equals(keys[index], key))
            {
                return index;
            }
        }

        return -1;
    }

    private static object? ResolveKey(BindingPath path, object? item)
    {
        if (item is null)
        {
            return null;
        }

        return path.TryResolve(item, out var value, out _, out _) ? value : null;
    }

    // Comparer.DefaultInvariant 能处理 null 与常见值类型；不可比较的类型按 .NET 约定抛异常。
    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(object? x, object? y) => Comparer.DefaultInvariant.Compare(x, y);
    }
}
