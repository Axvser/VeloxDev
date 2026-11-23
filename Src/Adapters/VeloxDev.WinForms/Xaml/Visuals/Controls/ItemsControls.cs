using System.Collections;
using System.Collections.Specialized;
using System.Drawing;
using System.Windows.Forms;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A panel that turns an <see cref="ItemsSource"/> into child visuals through a factory and stacks them vertically.</summary>
/// <remarks>
/// <para>
/// When the source also implements <see cref="INotifyCollectionChanged"/> every collection change rebuilds the
/// children; the first version does not diff individual add/remove operations. Each generated visual receives the
/// source item as its <see cref="VisualElement.DataContext"/> unless it already set one.
/// </para>
/// <para>
/// <see cref="Items"/> exposes the realized visuals. The factory must return a new, unparented element; this panel
/// owns the lifetime of whatever it returns.
/// </para>
/// <para>
/// A control template hosts the realized items in the first <see cref="ItemsPresenter"/> or panel marked with
/// <see cref="SetIsItemsHost"/> found inside the template root; a template root that is itself a panel acts as the
/// host when it declares none. Applying or clearing a template re-parents the realized items into the new host.
/// </para>
/// </remarks>
public class ItemsControl : TemplatedControl
{
    /// <summary>The sequence whose items become child visuals.</summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource), typeof(IEnumerable), typeof(ItemsControl),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Creates a visual from a source item; <see langword="null"/> uses a text projection.</summary>
    public static readonly DependencyProperty ItemFactoryProperty =
        DependencyProperty.Register(
            nameof(ItemFactory), typeof(Func<object, VisualElement>), typeof(ItemsControl),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The property read from each item for its text; empty uses <see cref="object.ToString"/>.</summary>
    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath), typeof(string), typeof(ItemsControl),
            new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The template that creates each item visual; takes precedence over <see cref="ItemFactory"/>.</summary>
    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(
            nameof(ItemTemplate), typeof(DataTemplate), typeof(ItemsControl),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Chooses the item template per source item; a non-null result wins over <see cref="ItemTemplate"/>.</summary>
    public static readonly DependencyProperty ItemTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(ItemTemplateSelector), typeof(DataTemplateSelector), typeof(ItemsControl),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The panel that hosts the realized items, or <see langword="null"/> to stack them vertically.</summary>
    public static readonly DependencyProperty ItemsPanelProperty =
        DependencyProperty.Register(
            nameof(ItemsPanel), typeof(ItemsPanelTemplate), typeof(ItemsControl),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Marks a panel as the host of an <see cref="ItemsControl"/>'s realized items.</summary>
    public static readonly DependencyProperty IsItemsHostProperty =
        DependencyProperty.RegisterAttached(
            "IsItemsHost", typeof(bool), typeof(ItemsControl),
            new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = false });

    private readonly List<object?> _sourceItems = [];
    private readonly List<VisualElement> _items = [];
    private INotifyCollectionChanged? _observed;
    private ItemsPanelTemplate? _appliedPanelTemplate;
    private Panel? _itemsHost;
    private bool _rebuilding;

    /// <summary>Creates an empty items panel.</summary>
    public ItemsControl()
    {
        // Panel 的默认是不拦空白处的指针；模板根自己决定可命中区域。
        IsHitTestVisible = false;
    }

    /// <summary>Reads whether <paramref name="element"/> is marked as an items host panel.</summary>
    /// <param name="element">The element to read.</param>
    public static bool GetIsItemsHost(VisualElement element)
        => element is null ? throw new ArgumentNullException(nameof(element)) : (bool)element.GetValue(IsItemsHostProperty)!;

    /// <summary>Marks <paramref name="element"/> as the host of an items control's realized items.</summary>
    /// <param name="element">The panel to mark.</param>
    /// <param name="value">Whether the panel hosts items.</param>
    public static void SetIsItemsHost(VisualElement element, bool value)
        => (element ?? throw new ArgumentNullException(nameof(element))).SetValue(IsItemsHostProperty, value);

    /// <summary>The sequence whose items become child visuals.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Creates a visual from a source item; <see langword="null"/> uses a text projection.</summary>
    public Func<object, VisualElement>? ItemFactory
    {
        get => (Func<object, VisualElement>?)GetValue(ItemFactoryProperty);
        set => SetValue(ItemFactoryProperty, value);
    }

    /// <summary>The property read from each item for its text; empty uses <see cref="object.ToString"/>.</summary>
    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty)!;
        set => SetValue(DisplayMemberPathProperty, value ?? string.Empty);
    }

    /// <summary>The template that creates each item visual; takes precedence over <see cref="ItemFactory"/>.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Chooses the item template per source item; a non-null result wins over <see cref="ItemTemplate"/>.</summary>
    public DataTemplateSelector? ItemTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ItemTemplateSelectorProperty);
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    /// <summary>The panel that hosts the realized items, or <see langword="null"/> to stack them vertically.</summary>
    public ItemsPanelTemplate? ItemsPanel
    {
        get => (ItemsPanelTemplate?)GetValue(ItemsPanelProperty);
        set => SetValue(ItemsPanelProperty, value);
    }

    /// <summary>The realized item visuals, in source order.</summary>
    public IReadOnlyList<VisualElement> Items => _items;

    /// <summary>The source items snapshotted from <see cref="ItemsSource"/>.</summary>
    protected IReadOnlyList<object?> SourceItems => _sourceItems;

    /// <summary>The number of source items, independent of how many are realized.</summary>
    protected int ItemCount => _sourceItems.Count;

    /// <summary>Rebuilds the realized item visuals from the current source and factory.</summary>
    public void RefreshItems() => RebuildItems();

    /// <summary>Raised after the realized item visuals were rebuilt.</summary>
    public event EventHandler? ItemsRebuilt;

    /// <summary>Returns the source item at <paramref name="index"/>, or <see langword="null"/> when out of range.</summary>
    /// <param name="index">The zero-based source index.</param>
    protected object? GetItemAt(int index)
        => index >= 0 && index < _sourceItems.Count ? _sourceItems[index] : null;

    /// <summary>Creates the visual for one source item.</summary>
    /// <param name="item">The source item; may be <see langword="null"/>.</param>
    /// <returns>The new, unparented item visual.</returns>
    /// <exception cref="InvalidOperationException">The configured <see cref="ItemFactory"/> returned <see langword="null"/>.</exception>
    protected virtual VisualElement CreateItemVisual(object? item)
    {
        var selector = ItemTemplateSelector;
        var template = selector?.SelectTemplate(item, null) ?? ItemTemplate;
        var factory = ItemFactory;
        var visual = template is not null
            ? template.Create()
            : factory is not null
                ? factory(item!)
                : new TextBlock
                {
                    Text = FormatItem(item),
                    Padding = new XamlThickness(8f, 4f),
                };

        if (visual is null)
        {
            throw new InvalidOperationException("ItemFactory returned null.");
        }

        if (item is not null && !visual.IsSet(VisualElement.DataContextProperty))
        {
            visual.DataContext = item;
        }

        return visual;
    }

    /// <summary>Realizes the current source items as children; the base implementation realizes every item.</summary>
    protected virtual void RealizeItems()
    {
        foreach (var item in _sourceItems)
        {
            AddRealizedItem(CreateItemVisual(item));
        }
    }

    /// <summary>Removes every realized item visual from its host.</summary>
    protected void ClearRealizedItems()
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            // items 可能挂在 ItemsPresenter/ItemsPanel 里，而不是控件自身。
            _items[index].ParentElement?.RemoveChild(_items[index]);
        }

        _items.Clear();
    }

    /// <summary>Adds a realized item visual to <see cref="Items"/> and to the active items host.</summary>
    /// <param name="visual">The item visual to add.</param>
    protected void AddRealizedItem(VisualElement visual)
    {
        if (visual is null) throw new ArgumentNullException(nameof(visual));
        _items.Add(visual);
        var host = ResolveItemsHost();
        if (host is not null && !ReferenceEquals(host, visual))
        {
            host.Add(visual);
            return;
        }

        Add(visual);
    }

    // ItemsPanel 优先；否则使用模板里的 ItemsPresenter/IsItemsHost 面板；都没有就由控件自己竖排。
    private Panel? ResolveItemsHost()
    {
        var template = ItemsPanel;
        if (template is not null)
        {
            if (_itemsHost is not null && ReferenceEquals(_appliedPanelTemplate, template))
            {
                return _itemsHost;
            }

            if (_itemsHost is not null && Children.Contains(_itemsHost))
            {
                RemoveChild(_itemsHost);
            }

            _appliedPanelTemplate = template;
            _itemsHost = template.Create();
            Add(_itemsHost);
            return _itemsHost;
        }

        var hostFromTemplate = _appliedPanelTemplate is not null && _itemsHost is not null;
        _appliedPanelTemplate = null;
        if (template is null && hostFromTemplate && Children.Contains(_itemsHost!))
        {
            // 上一轮由 ItemsPanel 创建的宿主在模板被移除后退出。
            RemoveChild(_itemsHost!);
            _itemsHost = null;
        }

        // 控件模板的宿主在模板根内部；先按显式标记找，找不到时把模板根本身当宿主。
        if (TemplateRoot is { } templateRoot)
        {
            _itemsHost = FindTemplateItemsHost(templateRoot)
                ?? (templateRoot as Panel);
            return _itemsHost;
        }

        foreach (var child in Children)
        {
            if (child is ItemsPresenter presenter)
            {
                _itemsHost = presenter;
                return presenter;
            }
        }

        foreach (var child in Children)
        {
            if (child is Panel panel && GetIsItemsHost(panel))
            {
                _itemsHost = panel;
                return panel;
            }
        }

        _itemsHost = null;
        return null;
    }

    private static Panel? FindTemplateItemsHost(VisualElement root)
    {
        if (root is ItemsPresenter presenter)
        {
            return presenter;
        }

        if (root is Panel panel && GetIsItemsHost(panel))
        {
            return panel;
        }

        foreach (var child in root.Children)
        {
            if (child is not VisualElement element)
            {
                continue;
            }

            if (FindTemplateItemsHost(element) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Called after <see cref="ItemsSource"/> or <see cref="ItemFactory"/> rebuilt the realized items.</summary>
    protected virtual void OnItemsRebuilt()
    {
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsTemplated)
        {
            return MeasureTemplate(availableSize);
        }

        var padding = Padding;
        var contentWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal));
        var contentHeight = Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical));
        if (_itemsHost is not null)
        {
            var hosted = _itemsHost.Measure(new Size(contentWidth, contentHeight));
            return new Size(
                hosted.Width + (int)Math.Ceiling(padding.Horizontal),
                hosted.Height + (int)Math.Ceiling(padding.Vertical));
        }

        var width = 0;
        var height = 0;
        foreach (var child in _items)
        {
            if (child.Visibility == XamlVisibility.Collapsed)
            {
                continue;
            }

            var measured = child.Measure(new Size(contentWidth, 0));
            width = Math.Max(width, measured.Width);
            height += measured.Height;
        }

        return new Size(
            width + (int)Math.Ceiling(padding.Horizontal),
            height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (IsTemplated)
        {
            ArrangeTemplate(finalSize);
            return;
        }

        var padding = Padding;
        var content = new RectangleF(
            (int)Math.Ceiling(padding.Left),
            (int)Math.Ceiling(padding.Top),
            Math.Max(0f, finalSize.Width - padding.Horizontal),
            Math.Max(0f, finalSize.Height - padding.Vertical));
        if (_itemsHost is not null)
        {
            _itemsHost.Arrange(content);
            return;
        }

        var position = content.Top;
        foreach (var child in _items)
        {
            if (child.Visibility == XamlVisibility.Collapsed)
            {
                continue;
            }

            var desired = child.DesiredSize;
            child.Arrange(new RectangleF(content.Left, position, content.Width, desired.Height));
            position += desired.Height;
        }
    }

    /// <inheritdoc />
    protected override void OnTemplateChanging()
    {
        base.OnTemplateChanging();

        // 模板根销毁前把已实现的 item 摘出来并释放；重建会创建新的 item 视觉，不能留在旧宿主里。
        foreach (var item in _items.ToArray())
        {
            item.Dispose();
        }

        ClearRealizedItems();
    }

    /// <inheritdoc />
    protected override void OnTemplateChanged()
    {
        base.OnTemplateChanged();
        RefreshItems();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ItemsSourceProperty))
        {
            Observe(e.OldValue as INotifyCollectionChanged, e.NewValue as INotifyCollectionChanged);
            RebuildItems();
        }
        else if (ReferenceEquals(e.Property, ItemFactoryProperty)
                 || ReferenceEquals(e.Property, ItemTemplateProperty)
                 || ReferenceEquals(e.Property, ItemTemplateSelectorProperty)
                 || ReferenceEquals(e.Property, ItemsPanelProperty)
                 || ReferenceEquals(e.Property, DisplayMemberPathProperty))
        {
            RebuildItems();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Observe(_observed, null);
        }

        base.Dispose(disposing);
    }

    private string FormatItem(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(DisplayMemberPath))
        {
            var property = item.GetType().GetProperty(DisplayMemberPath);
            if (property?.GetValue(item) is { } value)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        return item.ToString() ?? string.Empty;
    }

    private void RebuildItems()
    {
        if (_rebuilding)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            ClearRealizedItems();
            _sourceItems.Clear();
            if (ItemsSource is { } source)
            {
                foreach (var item in source)
                {
                    _sourceItems.Add(item);
                }
            }

            // 先确定宿主：空 ItemsSource 也要让 ItemsPanel 里的空面板参与布局。
            ResolveItemsHost();
            RealizeItems();
        }
        finally
        {
            _rebuilding = false;
        }

        InvalidateMeasure();
        OnItemsRebuilt();
        ItemsRebuilt?.Invoke(this, EventArgs.Empty);
    }

    private void Observe(INotifyCollectionChanged? previous, INotifyCollectionChanged? next)
    {
        if (ReferenceEquals(previous, next))
        {
            return;
        }

        if (previous is not null)
        {
            previous.CollectionChanged -= OnSourceCollectionChanged;
        }

        _observed = next;
        if (next is not null)
        {
            next.CollectionChanged += OnSourceCollectionChanged;
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildItems();
}

/// <summary>A vertical list whose items can be selected by pointer or keyboard.</summary>
public class ListBox : ItemsControl
{
    /// <summary>The zero-based index of the selected item, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex), typeof(int), typeof(ListBox),
            new PropertyMetadata(-1) { AffectsRender = true });

    /// <summary>The selected source item, or <see langword="null"/> when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem), typeof(object), typeof(ListBox),
            new PropertyMetadata(null) { AffectsRender = true });

    private bool _syncingSelection;

    /// <summary>Creates a focusable list with theme metrics.</summary>
    public ListBox()
    {
        Focusable = true;
        IsHitTestVisible = true;
        MinHeight = Theme.Current.ControlHeight;
    }

    /// <summary>Raised after <see cref="SelectedIndex"/> or <see cref="SelectedItem"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The zero-based index of the selected item, or -1 when nothing is selected.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The selected source item, or <see langword="null"/> when nothing is selected.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
    protected virtual void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (IsTemplated)
        {
            // 选中带属于自绘外观；模板用 SelectedIndex/SelectedItem 触发器表达选中态。
            return;
        }

        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
        {
            return;
        }

        var bounds = Items[SelectedIndex].Bounds;
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        using var path = XamlDrawing.RoundedRectangle(bounds, 4f);
        using var brush = new SolidBrush(Theme.Current.Selection);
        graphics.FillPath(brush, path);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        var index = IndexAt(args.GetPosition(this));
        if (index < 0)
        {
            return;
        }

        Focus();
        SelectedIndex = index;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (!IsEnabled || ItemCount == 0)
        {
            return;
        }

        var index = SelectedIndex;
        switch (args.KeyCode)
        {
            case Keys.Up:
                index = index <= 0 ? 0 : index - 1;
                break;
            case Keys.Down:
                index = index < 0 ? 0 : index + 1;
                break;
            case Keys.Home:
                index = 0;
                break;
            case Keys.End:
                index = ItemCount - 1;
                break;
            default:
                return;
        }

        SelectedIndex = XamlMath.Clamp(index, 0, ItemCount - 1);
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_syncingSelection)
        {
            return;
        }

        if (ReferenceEquals(e.Property, SelectedIndexProperty))
        {
            SyncSelectedItemFromIndex();
            InvalidateVisual();
            OnSelectionChanged();
        }
        else if (ReferenceEquals(e.Property, SelectedItemProperty))
        {
            SyncSelectedIndexFromItem();
            InvalidateVisual();
            OnSelectionChanged();
        }
    }

    /// <inheritdoc />
    protected override void OnItemsRebuilt()
    {
        base.OnItemsRebuilt();
        if (SelectedIndex >= ItemCount)
        {
            SelectedIndex = -1;
        }
        else if (SelectedIndex >= 0)
        {
            SyncSelectedItemFromIndex();
        }
        else if (SelectedItem is not null)
        {
            SyncSelectedIndexFromItem();
        }

        InvalidateVisual();
    }

    private int IndexAt(PointF local)
    {
        // 模板里的 item 挂在 ItemsHost 下，Bounds 不是列表坐标；统一换到表面坐标再比。
        var surfacePoint = PointToSurface(local);
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index].AbsoluteBounds.Contains(surfacePoint))
            {
                return index;
            }
        }

        return -1;
    }

    private void SyncSelectedItemFromIndex()
    {
        _syncingSelection = true;
        try
        {
            var index = SelectedIndex;
            SelectedItem = index >= 0 && index < ItemCount ? GetItemAt(index) : null;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncSelectedIndexFromItem()
    {
        _syncingSelection = true;
        try
        {
            var index = -1;
            if (SelectedItem is { } item)
            {
                for (var candidate = 0; candidate < ItemCount; candidate++)
                {
                    if (Equals(GetItemAt(candidate), item))
                    {
                        index = candidate;
                        break;
                    }
                }
            }

            SelectedIndex = index;
        }
        finally
        {
            _syncingSelection = false;
        }
    }
}

/// <summary>A fixed-row virtualizing items panel that realizes only the rows visible in an ancestor <see cref="ScrollViewer"/>.</summary>
/// <remarks>
/// <para>
/// The panel reports the full item count as its desired height, so the scrolling ancestor sees the correct extent, but
/// it creates children only for the rows intersecting the viewport. The nearest ancestor <see cref="ScrollViewer"/>
/// supplies <see cref="ScrollViewer.VerticalOffset"/> and <see cref="ScrollViewer.ViewportHeight"/>; without one the
/// panel treats its arranged height as the viewport with no offset.
/// </para>
/// <para>
/// The limitation is deliberate: every row has the same <see cref="ItemHeight"/>, scrolling is vertical only, and a
/// change of the visible range rebuilds those visuals instead of recycling them. Use <see cref="ItemsControl"/> when
/// the list is small or its rows vary in height.
/// </para>
/// </remarks>
public class VirtualList : ItemsControl
{
    /// <summary>The fixed height of one row, in pixels.</summary>
    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight), typeof(double), typeof(VirtualList),
            new PropertyMetadata(24d) { AffectsMeasure = true, AffectsRender = false });

    private int _firstRealized = -1;
    private int _lastRealized = -1;

    /// <summary>Creates an empty virtualizing panel.</summary>
    public VirtualList()
    {
    }

    /// <summary>The fixed height of one row, in pixels.</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty)!;
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>The source index of the first realized row, or -1 when nothing is realized.</summary>
    public int FirstRealizedIndex => _firstRealized;

    /// <summary>The number of realized rows.</summary>
    public int RealizedCount => Items.Count;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var rowHeight = Math.Max(1d, ItemHeight);
        return new Size(
            Math.Max(0, availableSize.Width),
            (int)Math.Ceiling(SourceItems.Count * rowHeight + padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        var rowHeight = (float)Math.Max(1d, ItemHeight);
        var contentWidth = Math.Max(0f, finalSize.Width - padding.Horizontal);
        var (offset, viewport) = ResolveViewport(finalSize.Height);
        var (first, last) = ComputeRange(rowHeight, viewport, offset, padding.Top);
        EnsureRange(first, last);

        for (var index = first; index < last; index++)
        {
            var visual = Items[index - first];
            visual.Measure(new Size((int)Math.Ceiling(contentWidth), (int)Math.Ceiling(rowHeight)));
            visual.Arrange(new RectangleF(
                (int)Math.Ceiling(padding.Left),
                padding.Top + (index * rowHeight),
                contentWidth,
                rowHeight));
        }
    }

    /// <inheritdoc />
    protected override void RealizeItems()
    {
        // 真实行由 ArrangeOverride 按可见区间补齐；这里只重置区间记录。
        _firstRealized = -1;
        _lastRealized = -1;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(e.Property, TemplatedControl.TemplateProperty) && e.NewValue is not null)
        {
            // 直接给 Template 也拒绝：虚拟化面板没有可供模板接管的行宿主。
            ClearValue(TemplatedControl.TemplateProperty);
            throw new NotSupportedException(
                "VirtualList arranges a virtualized row window itself and cannot be re-skinned by a ControlTemplate.");
        }

        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ItemHeightProperty))
        {
            _firstRealized = -1;
            _lastRealized = -1;
        }
    }

    private void EnsureRange(int first, int last)
    {
        if (first == _firstRealized && last == _lastRealized && Items.Count == last - first)
        {
            return;
        }

        ClearRealizedItems();
        for (var index = first; index < last; index++)
        {
            AddRealizedItem(CreateItemVisual(GetItemAt(index)));
        }

        _firstRealized = first;
        _lastRealized = last;
    }

    private (int First, int Last) ComputeRange(float rowHeight, float viewport, float offset, float top)
    {
        var count = SourceItems.Count;
        if (count == 0)
        {
            return (0, 0);
        }

        var visible = Math.Max(rowHeight, viewport);
        var start = Math.Max(0f, offset - top);
        var first = (int)Math.Floor(start / rowHeight);
        var last = (int)Math.Ceiling((start + visible) / rowHeight);
        first = XamlMath.Clamp(first, 0, count);
        last = XamlMath.Clamp(last, first, count);
        return (first, last);
    }

    // 偏移量来自最近的滚动祖先；没有滚动祖先时整段高度就是视口。
    private (float Offset, float Viewport) ResolveViewport(float finalHeight)
    {
        for (var current = ParentElement; current is not null; current = current.ParentElement)
        {
            if (current is ScrollViewer scroll)
            {
                var viewport = scroll.ViewportHeight > 0f ? scroll.ViewportHeight : scroll.Bounds.Height;
                return (scroll.VerticalOffset, viewport > 0f ? viewport : finalHeight);
            }
        }

        return (0f, finalHeight);
    }

    /// <summary>Rejects a control template: rows are virtualized and arranged by this panel.</summary>
    /// <param name="template">The template a style supplies, or <see langword="null"/> to clear it.</param>
    /// <exception cref="NotSupportedException"><paramref name="template"/> is not <see langword="null"/>.</exception>
    internal override void ApplyStyleTemplate(VisualControlTemplate? template)
    {
        if (template is not null)
        {
            throw new NotSupportedException(
                "VirtualList arranges a virtualized row window itself; a ControlTemplate needs a recycling "
                + "row-container model that this panel does not have. Style its item height and colors instead.");
        }
    }
}
