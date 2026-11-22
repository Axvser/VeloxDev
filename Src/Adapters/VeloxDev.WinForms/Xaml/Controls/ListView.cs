using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NativeListView = System.Windows.Forms.ListView;
using NativeListViewItem = System.Windows.Forms.ListViewItem;
using NativeView = System.Windows.Forms.View;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A details list backed by a native <see cref="System.Windows.Forms.ListView"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="View"/> supplies the columns. Each <see cref="GridViewColumn"/> contributes a native column header and a
/// data path: <see cref="GridViewColumn.DisplayMemberBinding"/> reads the cell value, and when it is empty the first
/// column falls back to <see cref="DisplayMemberPath"/> and then to <see cref="object.ToString"/>.
/// </para>
/// <para>
/// Rows are rebuilt from <see cref="ItemsSource"/> and kept in sync when the source raises
/// <see cref="INotifyCollectionChanged"/>. Native column headers are painted by the system; the modern colours only
/// reach the client area and rows. Row height is approximated with a one-pixel-wide small image list because a
/// detailed list has no row-height property.
/// </para>
/// </remarks>
public class ListView : Control
{
    /// <summary>The sequence whose items become rows.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(ListView),
        new PropertyMetadata(null, OnItemsSourcePropertyChanged));

    /// <summary>The selected item, or null when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(ListView),
        new PropertyMetadata(null, OnSelectionPropertyChanged));

    /// <summary>The selected row index, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(ListView),
        new PropertyMetadata(-1, OnSelectionPropertyChanged));

    /// <summary>The details description, or null for a plain list.</summary>
    public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(
        nameof(View),
        typeof(GridView),
        typeof(ListView),
        new PropertyMetadata(null, OnViewPropertyChanged));

    /// <summary>The property path used for the first column when that column has no binding; empty uses the item's string form.</summary>
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath),
        typeof(string),
        typeof(ListView),
        new PropertyMetadata(string.Empty, OnStructurePropertyChanged));

    /// <summary>The border stroke colour; empty uses the active theme border.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(ListView),
        new PropertyMetadata(Color.Empty, OnChromePropertyChanged));

    /// <summary>The border stroke width per side, in pixels.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(ListView),
        new PropertyMetadata(new Thickness(1f), OnChromePropertyChanged));

    /// <summary>The corner radius, in pixels; six by default.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(ListView),
        new PropertyMetadata(6f, OnChromePropertyChanged));

    private readonly NativeListView _list;
    private readonly ImageList _rowHeight;
    private readonly List<INotifyCollectionChanged> _observed = [];
    private readonly List<NativeListViewItem> _generated = [];
    private INotifyCollectionChanged? _viewColumns;
    private bool _syncing;

    /// <summary>Creates the wrapper and its native list.</summary>
    public ListView()
    {
        TabStop = false;

        _list = new NativeListView
        {
            BorderStyle = BorderStyle.None,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            TabStop = true,
        };

        // 详细信息视图没有行高属性；小图标列表的图标高度是系统用来算行高的那一项。
        _rowHeight = new ImageList
        {
            ImageSize = new Size(1, Math.Max(1, Theme.Current.ControlHeight - 8)),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        _list.SmallImageList = _rowHeight;

        _list.SelectedIndexChanged += OnSelectedIndexChanged;
        _list.KeyDown += (_, e) => OnKeyDown(e);
        _list.KeyUp += (_, e) => OnKeyUp(e);
        _list.MouseDown += (_, e) => OnMouseDown(e);
        _list.MouseUp += (_, e) => OnMouseUp(e);
        _list.MouseMove += (_, e) => OnMouseMove(e);
        _list.MouseEnter += (_, e) => OnMouseEnter(e);
        _list.MouseLeave += (_, e) => OnMouseLeave(e);
        _list.MouseWheel += (_, e) => OnMouseWheel(e);
        _list.GotFocus += (_, e) => OnGotFocus(e);
        _list.LostFocus += (_, e) => OnLostFocus(e);

        Controls.Add(_list);
        ApplyNativeAppearance();
    }

    /// <summary>Gets or sets the sequence whose items become rows.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Gets or sets the selected item.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Gets or sets the selected row index.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Gets or sets the details description.</summary>
    public GridView? View
    {
        get => (GridView?)GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    /// <summary>Gets or sets the first-column display path used when the column has no binding.</summary>
    public string DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty) ?? string.Empty;
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>Gets or sets the border stroke colour.</summary>
    public Color BorderBrush
    {
        get => (Color)GetValue(BorderBrushProperty)!;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>Gets or sets the corner radius in pixels.</summary>
    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>The native row collection, for hand-authored <see cref="ListViewItem"/> rows.</summary>
    /// <remarks>Use either <see cref="ItemsSource"/> or this collection; rows added here survive a source rebuild.</remarks>
    public IList Items => _list.Items;

    /// <summary>The number of rows in the native list.</summary>
    public int ItemCount => _list.Items.Count;

    /// <summary>Raised after the native list reports a selection change.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Moves focus to the native list.</summary>
    /// <returns><see langword="true"/> when the list received focus.</returns>
    public new bool Focus() => Focusable && _list.Focus();

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = _list.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            preferred.Width + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            preferred.Height + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var content = XamlLayout.ContentBounds(this);
        var border = BorderThickness;
        var inset = new Rectangle(
            content.Left + (int)Math.Ceiling(border.Left),
            content.Top + (int)Math.Ceiling(border.Top),
            Math.Max(0, content.Width - (int)Math.Ceiling(border.Horizontal)),
            Math.Max(0, content.Height - (int)Math.Ceiling(border.Vertical)));
        if (_list.Bounds != inset)
        {
            _list.Bounds = inset;
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsPainted)
        {
            return;
        }

        XamlDrawing.PaintChrome(
            e.Graphics,
            ClientRectangle,
            EffectiveBackground,
            EffectiveBorder,
            BorderThickness,
            CornerRadius);
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);
        ApplyNativeAppearance();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachObservers();
            _list.SelectedIndexChanged -= OnSelectedIndexChanged;
            if (Controls.Contains(_list))
            {
                Controls.Remove(_list);
            }

            _list.SmallImageList = null;
            _list.Dispose();
            _rowHeight.Dispose();
        }

        base.Dispose(disposing);
    }

    private new Color EffectiveBackground => HasXamlValue(BackgroundProperty) && Background is { A: > 0 } brush
        ? brush.ApproximateColor
        : Theme.Current.Surface;

    private new Color EffectiveForeground => HasXamlValue(ForegroundProperty) ? base.EffectiveForeground : Theme.Current.Foreground;

    private Color EffectiveBorder => HasXamlValue(BorderBrushProperty) ? BorderBrush : Theme.Current.ControlBorder;

    private void ApplyNativeAppearance()
    {
        if (IsDisposed || _list.IsDisposed)
        {
            return;
        }

        _list.BackColor = XamlDrawing.ToOpaque(EffectiveBackground, SystemColors.Window);
        _list.ForeColor = EffectiveForeground;
        _list.Font = Font;
        _list.Enabled = IsEnabled;
        Invalidate();
    }

    private void Rebuild()
    {
        if (IsDisposed)
        {
            return;
        }

        _syncing = true;
        try
        {
            _list.BeginUpdate();
            try
            {
                _list.Columns.Clear();
                var grid = View;
                if (grid is { } details)
                {
                    _list.View = NativeView.Details;
                    for (var index = 0; index < details.Columns.Count; index++)
                    {
                        var column = details.Columns[index];
                        _list.Columns.Add(new ColumnHeader
                        {
                            Text = string.IsNullOrEmpty(column.Header) ? "Column " + (index + 1) : column.Header,
                            Width = column.Width > 0 ? column.Width : 120,
                        });
                    }
                }
                else
                {
                    _list.View = NativeView.List;
                }

                // 只移除上一次由源生成的行；作者手写在 Items 里的行原样保留。
                foreach (var row in _generated)
                {
                    _list.Items.Remove(row);
                }

                _generated.Clear();
                if (ItemsSource is { } source)
                {
                    foreach (var item in source)
                    {
                        var row = CreateRow(item, grid);
                        _list.Items.Add(row);
                        _generated.Add(row);
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }
        finally
        {
            _syncing = false;
        }

        ApplySelection();
    }

    private NativeListViewItem CreateRow(object? item, GridView? grid)
    {
        var row = new NativeListViewItem
        {
            Text = GetColumnText(item, 0, grid),
            Tag = item,
        };

        var columnCount = grid?.Columns.Count ?? 0;
        for (var index = 1; index < columnCount; index++)
        {
            row.SubItems.Add(GetColumnText(item, index, grid));
        }

        return row;
    }

    private string GetColumnText(object? item, int columnIndex, GridView? grid)
    {
        if (item is null)
        {
            return string.Empty;
        }

        string? path = null;
        if (grid is not null && columnIndex >= 0 && columnIndex < grid.Columns.Count)
        {
            path = grid.Columns[columnIndex].DisplayMemberBinding;
        }

        if (string.IsNullOrEmpty(path) && columnIndex == 0)
        {
            path = DisplayMemberPath;
        }

        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                var bindingPath = new BindingPath(path);
                if (bindingPath.TryResolve(item, out var value, out _, out _))
                {
                    return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
                }
            }
            catch (Exception)
            {
                // 显示路径写错时留空，不要让整个列表建不出来。
                return string.Empty;
            }
        }

        return columnIndex == 0 ? Convert.ToString(item, CultureInfo.CurrentCulture) ?? string.Empty : string.Empty;
    }

    private void OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_syncing || IsDisposed)
        {
            return;
        }

        _syncing = true;
        try
        {
            var index = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
            var item = index >= 0 ? _list.Items[index].Tag : null;
            SetValue(SelectedIndexProperty, index);
            SetValue(SelectedItemProperty, item);
        }
        finally
        {
            _syncing = false;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySelection()
    {
        if (IsDisposed || _syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            var index = -1;
            if (SelectedItem is { } item)
            {
                index = FindIndexByTag(item);
            }
            else if (SelectedIndex >= 0 && SelectedIndex < _list.Items.Count)
            {
                index = SelectedIndex;
            }

            _list.SelectedIndices.Clear();
            if (index >= 0)
            {
                _list.Items[index].Selected = true;
                _list.EnsureVisible(index);

                if (SelectedIndex != index)
                {
                    SetValue(SelectedIndexProperty, index);
                }

                var tag = _list.Items[index].Tag;
                if (!Equals(SelectedItem, tag))
                {
                    SetValue(SelectedItemProperty, tag);
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private int FindIndexByTag(object item)
    {
        for (var index = 0; index < _list.Items.Count; index++)
        {
            if (ReferenceEquals(_list.Items[index].Tag, item))
            {
                return index;
            }
        }

        for (var index = 0; index < _list.Items.Count; index++)
        {
            if (Equals(_list.Items[index].Tag, item))
            {
                return index;
            }
        }

        return -1;
    }

    private void AttachSource(IEnumerable? source)
    {
        DetachSource();
        if (source is INotifyCollectionChanged observable)
        {
            _observed.Add(observable);
            observable.CollectionChanged += OnCollectionChanged;
        }
    }

    private void DetachSource()
    {
        foreach (var observable in _observed)
        {
            observable.CollectionChanged -= OnCollectionChanged;
        }

        _observed.Clear();
    }

    private void DetachObservers()
    {
        DetachSource();

        if (_viewColumns is not null)
        {
            _viewColumns.CollectionChanged -= OnViewColumnsChanged;
            _viewColumns = null;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnViewColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void AttachViewColumns(GridView? view)
    {
        if (_viewColumns is not null)
        {
            _viewColumns.CollectionChanged -= OnViewColumnsChanged;
        }

        _viewColumns = view?.Columns as INotifyCollectionChanged;
        if (_viewColumns is not null)
        {
            _viewColumns.CollectionChanged += OnViewColumnsChanged;
        }
    }

    private static void OnItemsSourcePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ListView list && !list.IsDisposed)
        {
            list.AttachSource(e.NewValue as IEnumerable);
            list.Rebuild();
        }
    }

    private static void OnSelectionPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ListView list && !list.IsDisposed && !list._syncing)
        {
            list.ApplySelection();
        }
    }

    private static void OnViewPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ListView list && !list.IsDisposed)
        {
            list.AttachViewColumns(e.NewValue as GridView);
            list.Rebuild();
        }
    }

    private static void OnStructurePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ListView list && !list.IsDisposed)
        {
            list.Rebuild();
        }
    }

    private static void OnChromePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ListView list && !list.IsDisposed)
        {
            list.ApplyNativeAppearance();
        }
    }
}

/// <summary>
/// The column description of a <see cref="ListView"/>.
/// </summary>
/// <remarks>Columns can be added in XAML through the read-only <see cref="Columns"/> collection.</remarks>
public class GridView
{
    /// <summary>The columns, left to right.</summary>
    public IList<GridViewColumn> Columns { get; } = new ObservableCollection<GridViewColumn>();
}

/// <summary>One column of a <see cref="GridView"/>.</summary>
public class GridViewColumn
{
    /// <summary>The column header text; empty uses a generated name.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>The property path read for each cell; empty falls back to the item's string form for the first column.</summary>
    public string DisplayMemberBinding { get; set; } = string.Empty;

    /// <summary>The column width in pixels; zero or less uses 120.</summary>
    public int Width { get; set; } = 120;
}

/// <summary>A row of a <see cref="ListView"/> declared in XAML.</summary>
/// <remarks>
/// This is a native <see cref="System.Windows.Forms.ListViewItem"/> with a public parameterless constructor so it can
/// be written as a child of <c>ListView.Items</c>. Rows declared this way are kept when the source is rebuilt.
/// </remarks>
public class ListViewItem : NativeListViewItem
{
    /// <summary>Creates an empty row.</summary>
    public ListViewItem()
    {
    }

    /// <summary>Creates a row with <paramref name="text"/> as its first column.</summary>
    /// <param name="text">The first-column text.</param>
    public ListViewItem(string? text)
        : base(text ?? string.Empty)
    {
    }
}
