using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A non-editable drop-down selector for the visual backend.</summary>
/// <remarks>
/// <para>
/// The closed control draws the selected item itself. Opening moves a <see cref="VisualPopup"/> holding a
/// <see cref="ScrollViewer"/> and a <see cref="ListBox"/> into the surface overlay, so the list is neither clipped by
/// an ancestor <see cref="ScrollViewer"/> nor painted under later siblings.
/// </para>
/// <para>
/// Logical focus stays on the combo box while the drop-down is open: the list and its viewer are not focusable, and
/// Up/Down/Home/End/PageUp/PageDown are forwarded to them. Enter commits and closes, Escape restores the selection
/// captured at open and closes.
/// </para>
/// </remarks>
public class ComboBox : VisualElement
{
    /// <summary>The sequence whose items fill the drop-down.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ComboBox),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Creates an item visual; <see langword="null"/> projects each item to a <see cref="TextBlock"/>.</summary>
    public static readonly DependencyProperty ItemFactoryProperty = DependencyProperty.Register(
        nameof(ItemFactory), typeof(Func<object, VisualElement>), typeof(ComboBox),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>The zero-based index of the selected item, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(ComboBox),
        new PropertyMetadata(-1) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The selected source item, or <see langword="null"/> when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ComboBox),
        new PropertyMetadata(null) { AffectsRender = true });

    /// <summary>Whether the drop-down is open.</summary>
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(ComboBox),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>The property read from each item for its display text; empty uses <see cref="object.ToString"/>.</summary>
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(ComboBox),
        new PropertyMetadata(string.Empty) { AffectsRender = true });

    /// <summary>The largest drop-down height, in pixels.</summary>
    public static readonly DependencyProperty MaxDropDownHeightProperty = DependencyProperty.Register(
        nameof(MaxDropDownHeight), typeof(double), typeof(ComboBox),
        new PropertyMetadata(200d) { AffectsRender = false });

    /// <summary>The text drawn while nothing is selected.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ComboBox),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    private readonly ObservableCollection<object?> _items = [];
    private readonly ListBox _list = new();
    private readonly ScrollViewer _scroll = new();
    private readonly VisualPopup _popup = new();
    private bool _hover;
    private bool _syncingSelection;
    private bool _syncingOpen;
    private int _selectionAtOpen = -1;

    /// <summary>Creates an empty, focusable selector with theme metrics.</summary>
    public ComboBox()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
        Padding = new XamlThickness(10f, 6f);
        MinHeight = Theme.Current.ControlHeight;
        CornerRadius = Theme.Current.CornerRadius;
        _items.CollectionChanged += OnItemsCollectionChanged;

        _list.Focusable = false;
        _list.ItemFactory = CreateItemVisual;
        _list.ItemsSource = _items;
        _list.SelectionChanged += OnListSelectionChanged;
        _list.ItemsRebuilt += OnListItemsRebuilt;
        _list.PointerPressed += OnListPointerPressed;

        _scroll.Focusable = false;
        _scroll.MaxHeight = 200d;
        _scroll.Content = _list;

        _popup.Child = _scroll;
        _popup.PlacementTarget = this;
        _popup.Placement = VisualPlacement.Bottom;
        _popup.StaysOpen = false;
        _popup.Closed += OnPopupClosed;
    }

    /// <summary>Raised after <see cref="SelectedIndex"/> or <see cref="SelectedItem"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The sequence whose items fill the drop-down; it wins over <see cref="Items"/>.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Creates an item visual; <see langword="null"/> projects each item to a <see cref="TextBlock"/>.</summary>
    public Func<object, VisualElement>? ItemFactory
    {
        get => (Func<object, VisualElement>?)GetValue(ItemFactoryProperty);
        set => SetValue(ItemFactoryProperty, value);
    }

    /// <summary>Items declared inline in XAML or added from code; used when <see cref="ItemsSource"/> is empty.</summary>
    public IList Items => _items;

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

    /// <summary>Whether the drop-down is open.</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>The property read from each item for its display text; empty uses <see cref="object.ToString"/>.</summary>
    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty)!;
        set => SetValue(DisplayMemberPathProperty, value ?? string.Empty);
    }

    /// <summary>The largest drop-down height, in pixels.</summary>
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty)!;
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>The text drawn while nothing is selected.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty)!;
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>The realized drop-down list; exposed for tests and hosts.</summary>
    public ListBox DropDownList => _list;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var text = string.IsNullOrEmpty(DisplayText) ? "M" : DisplayText;
        var measured = VisualText.Measure(text, DefaultFont, 0f, wrap: false, RenderDpi);
        var width = (int)Math.Ceiling(measured.Width + Padding.Horizontal + 26);
        var height = Math.Max(
            Theme.Current.ControlHeight,
            (int)Math.Ceiling(measured.Height + Padding.Vertical + 2));
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var background = Background?.ApproximateColor
            ?? (!IsEnabled
                ? theme.DisabledBackground
                : _hover || IsDropDownOpen
                    ? theme.ControlHover
                    : theme.ControlBackground);
        var border = BorderBrush?.ApproximateColor ?? theme.ControlBorder;
        var bounds = new Rectangle(0, 0, Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        var thickness = BorderThickness.IsEmpty ? new XamlThickness(1f) : BorderThickness;
        var radius = CornerRadius > 0 ? CornerRadius : theme.CornerRadius;
        XamlDrawing.PaintChrome(graphics, bounds, background, border, thickness, radius);

        if (IsFocused)
        {
            using var focusPath = XamlDrawing.RoundedRectangle(
                new RectangleF(1.5f, 1.5f, Math.Max(0f, bounds.Width - 3f), Math.Max(0f, bounds.Height - 3f)),
                Math.Max(0f, radius - 1f));
            using var focusPen = new Pen(theme.FocusRing, 1.6f);
            graphics.DrawPath(focusPen, focusPath);
        }

        var chevronRight = bounds.Width - 12f;
        var chevronCenter = bounds.Height / 2f;
        var foreground = IsEnabled ? theme.Foreground : theme.DisabledForeground;
        using (var chevron = new SolidBrush(foreground))
        {
            graphics.FillPolygon(chevron, new[]
            {
                new PointF(chevronRight - 4f, chevronCenter - 2f),
                new PointF(chevronRight + 4f, chevronCenter - 2f),
                new PointF(chevronRight, chevronCenter + 3f),
            });
        }

        var text = DisplayText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var content = new Rectangle(
            (int)Math.Ceiling(Padding.Left),
            (int)Math.Ceiling(Padding.Top),
            Math.Max(0, bounds.Width - (int)Math.Ceiling(Padding.Horizontal) - 18),
            Math.Max(0, bounds.Height - (int)Math.Ceiling(Padding.Vertical)));
        VisualText.Draw(this,
            graphics,
            text,
            DefaultFont,
            foreground,
            content,
            wrap: false,
            StringAlignment.Near,
            StringAlignment.Center);
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        base.OnPointerEntered(args);
        _hover = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        _hover = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        IsDropDownOpen = !IsDropDownOpen;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (!IsEnabled)
        {
            return;
        }

        switch (args.KeyCode)
        {
            case Keys.Down when (args.Modifiers & Keys.Alt) != 0:
            case Keys.F4:
                IsDropDownOpen = true;
                args.Handled = true;
                break;

            case Keys.Up when (args.Modifiers & Keys.Alt) != 0:
                IsDropDownOpen = false;
                args.Handled = true;
                break;

            case Keys.Escape when IsDropDownOpen:
                RestoreSelectionAtOpen();
                IsDropDownOpen = false;
                args.Handled = true;
                break;

            case Keys.Enter when IsDropDownOpen:
            case Keys.Space when IsDropDownOpen:
                IsDropDownOpen = false;
                args.Handled = true;
                break;

            case Keys.Down or Keys.Up or Keys.Home or Keys.End when IsDropDownOpen:
                _list.RaiseKeyDown(args);
                ScrollSelectionIntoView();
                break;

            case Keys.PageUp or Keys.PageDown when IsDropDownOpen:
                _scroll.RaiseKeyDown(args);
                break;

            case Keys.Down:
                MoveSelection(1);
                args.Handled = true;
                break;

            case Keys.Up:
                MoveSelection(-1);
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, ItemsSourceProperty))
        {
            _list.ItemsSource = ItemsSource ?? _items;
            return;
        }

        if (ReferenceEquals(e.Property, ItemFactoryProperty))
        {
            _list.ItemFactory = ItemFactory ?? CreateItemVisual;
            _list.RefreshItems();
            return;
        }

        if (ReferenceEquals(e.Property, DisplayMemberPathProperty))
        {
            _list.RefreshItems();
            InvalidateMeasure();
            return;
        }

        if (ReferenceEquals(e.Property, MaxDropDownHeightProperty))
        {
            _scroll.MaxHeight = Math.Max(Theme.Current.ControlHeight, MaxDropDownHeight);
            if (IsDropDownOpen)
            {
                _popup.Reposition();
            }

            return;
        }

        if (ReferenceEquals(e.Property, SelectedIndexProperty))
        {
            SyncSelectedFromIndex();
            SyncListSelection();
            InvalidateMeasure();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (ReferenceEquals(e.Property, SelectedItemProperty))
        {
            SyncSelectedIndexFromItem();
            SyncListSelection();
            InvalidateMeasure();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (ReferenceEquals(e.Property, IsDropDownOpenProperty))
        {
            if (_syncingOpen)
            {
                return;
            }

            if (IsDropDownOpen)
            {
                _selectionAtOpen = SelectedIndex;
                SyncListSelection();
                _popup.IsOpen = true;
                ScrollSelectionIntoView();
            }
            else
            {
                _popup.IsOpen = false;
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _items.CollectionChanged -= OnItemsCollectionChanged;
            _list.SelectionChanged -= OnListSelectionChanged;
            _list.ItemsRebuilt -= OnListItemsRebuilt;
            _list.PointerPressed -= OnListPointerPressed;
            _popup.Closed -= OnPopupClosed;
            _popup.Dispose();
        }

        base.Dispose(disposing);
    }

    private string DisplayText
    {
        get
        {
            var item = SelectedItem;
            if (item is not null)
            {
                return FormatItem(item);
            }

            return Placeholder;
        }
    }

    // ItemsSource 非空时压过 Items；ListBox 直接观察这个集合，集合变化会重建行。
    private IEnumerable EffectiveItems => ItemsSource ?? _items;

    private VisualElement CreateItemVisual(object item)
    {
        if (ItemFactory is { } factory)
        {
            return factory(item);
        }

        return item as VisualElement
            ?? new TextBlock
            {
                Text = FormatItem(item),
                Padding = new XamlThickness(8f, 4f),
            };
    }

    private string FormatItem(object item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(DisplayMemberPath))
        {
            var property = item.GetType().GetProperty(
                DisplayMemberPath,
                BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(item) is { } value)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        return item.ToString() ?? string.Empty;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ItemsSource is not null)
        {
            return;
        }

        _list.ItemsSource = _items;

        // 选择可能早于条目添加（XAML/初始化器先写 SelectedIndex）；条目够到索引后再解析。
        if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
        {
            SyncSelectedFromIndex();
        }
        else if (SelectedIndex >= 0 && SelectedItem is not null)
        {
            // 集合缩短后原来的选中项已经不存在，别留下一个指向空位的高亮。
            SelectedIndex = -1;
        }
    }

    private void OnListItemsRebuilt(object? sender, EventArgs e)
    {
        if (SelectedIndex >= 0 && SelectedIndex < Count(EffectiveItems))
        {
            SyncListSelection();
        }
    }

    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        // 重建期间 ListBox 先把选择清成 -1；待定索引尚未解析出对象时保留它，条目到位后再推回去。
        if (_list.SelectedIndex < 0 && SelectedIndex >= 0 && SelectedItem is null)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            SelectedIndex = _list.SelectedIndex;
            SelectedItem = _list.SelectedItem;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // 指针选中一行即提交：ListBox 已经写好 SelectedIndex，事件在这里关闭弹层。
    private void OnListPointerPressed(object? sender, VisualPointerEventArgs args)
    {
        if (IsDropDownOpen && args.Button == MouseButtons.Left)
        {
            IsDropDownOpen = false;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (IsDropDownOpen)
        {
            _syncingOpen = true;
            IsDropDownOpen = false;
            _syncingOpen = false;
        }

        Focus();
    }

    private void SyncSelectedFromIndex()
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            var index = SelectedIndex;
            var items = EffectiveItems;
            SelectedItem = index >= 0 && index < Count(items) ? ItemAt(items, index) : null;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncSelectedIndexFromItem()
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            var index = -1;
            if (SelectedItem is { } selected)
            {
                var items = EffectiveItems;
                var count = Count(items);
                for (var candidate = 0; candidate < count; candidate++)
                {
                    if (Equals(ItemAt(items, candidate), selected))
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

    private void SyncListSelection()
    {
        if (_list.SelectedIndex == SelectedIndex && Equals(_list.SelectedItem, SelectedItem))
        {
            return;
        }

        // 行还没物化时写一个越界索引会被 ListBox 立刻清成 -1；等 ItemsRebuilt 再推。
        if (SelectedIndex >= _list.Items.Count)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            _list.SelectedIndex = SelectedIndex;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void MoveSelection(int delta)
    {
        var count = Count(EffectiveItems);
        if (count == 0)
        {
            return;
        }

        var current = SelectedIndex;
        var next = current < 0
            ? (delta > 0 ? 0 : count - 1)
            : XamlMath.Clamp(current + delta, 0, count - 1);
        SelectedIndex = next;
    }

    private void RestoreSelectionAtOpen()
    {
        if (_selectionAtOpen != SelectedIndex)
        {
            SelectedIndex = _selectionAtOpen;
        }
    }

    private void ScrollSelectionIntoView()
    {
        var index = SelectedIndex;
        if (_popup.IsOpen && index >= 0 && index < _list.Items.Count)
        {
            _scroll.ScrollIntoView(_list.Items[index]);
        }
    }

    private static int Count(IEnumerable source)
    {
        if (source is ICollection collection)
        {
            return collection.Count;
        }

        var count = 0;
        foreach (var _ in source)
        {
            count++;
        }

        return count;
    }

    private static object? ItemAt(IEnumerable source, int index)
    {
        if (source is IList list)
        {
            return list[index];
        }

        var current = 0;
        foreach (var item in source)
        {
            if (current++ == index)
            {
                return item;
            }
        }

        return null;
    }
}

/// <summary>An inline item for <see cref="ComboBox.Items"/>; its text is its display text.</summary>
public class ComboBoxItem : TextBlock
{
    /// <summary>Creates an empty item with the drop-down row padding.</summary>
    public ComboBoxItem()
    {
        Padding = new XamlThickness(8f, 4f);
    }
}
