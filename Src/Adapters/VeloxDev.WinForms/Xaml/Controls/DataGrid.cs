using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NativeDataGrid = System.Windows.Forms.DataGridView;
using NativeColumn = System.Windows.Forms.DataGridViewTextBoxColumn;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A read-only tabular view backed by a native <see cref="System.Windows.Forms.DataGridView"/>.
/// </summary>
/// <remarks>
/// <para>
/// Columns come from <see cref="Columns"/> when it is not empty; otherwise <see cref="AutoGenerateColumns"/> infers a
/// column per readable public property of the first item. Each <see cref="DataGridColumn.Binding"/> is a property path;
/// an empty path falls back to <see cref="DataGridColumn.Header"/>. Rows are rebuilt from <see cref="ItemsSource"/> and
/// follow <see cref="INotifyCollectionChanged"/>.
/// </para>
/// <para>
/// This is a display surface only. Editing, validation, sorting, grouping and virtual mode are not implemented; the
/// native grid is read-only and row headers are hidden.
/// </para>
/// </remarks>
public class DataGrid : Control
{
    /// <summary>The sequence whose items become rows.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(DataGrid),
        new PropertyMetadata(null, OnItemsSourcePropertyChanged));

    /// <summary>Whether columns are inferred from the first item when <see cref="Columns"/> is empty.</summary>
    public static readonly DependencyProperty AutoGenerateColumnsProperty = DependencyProperty.Register(
        nameof(AutoGenerateColumns),
        typeof(bool),
        typeof(DataGrid),
        new PropertyMetadata(true, OnStructurePropertyChanged));

    /// <summary>The selected item, or null when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(DataGrid),
        new PropertyMetadata(null, OnSelectionPropertyChanged));

    /// <summary>The selected row index, or -1 when nothing is selected.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(DataGrid),
        new PropertyMetadata(-1, OnSelectionPropertyChanged));

    /// <summary>The border stroke colour; empty uses the active theme border.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(DataGrid),
        new PropertyMetadata(Color.Empty, OnChromePropertyChanged));

    /// <summary>The border stroke width per side, in pixels.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(DataGrid),
        new PropertyMetadata(new Thickness(1f), OnChromePropertyChanged));

    /// <summary>The corner radius, in pixels; six by default.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(DataGrid),
        new PropertyMetadata(6f, OnChromePropertyChanged));

    private readonly NativeDataGrid _grid;
    private readonly ObservableCollection<DataGridColumn> _columns = new();
    private INotifyCollectionChanged? _observed;
    private bool _syncing;

    /// <summary>Creates the wrapper and its native grid.</summary>
    public DataGrid()
    {
        TabStop = false;

        _grid = new NativeDataGrid
        {
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false,
            TabStop = true,
        };

        _grid.SelectionChanged += OnSelectionChanged;
        _grid.KeyDown += (_, e) => OnKeyDown(e);
        _grid.KeyUp += (_, e) => OnKeyUp(e);
        _grid.MouseDown += (_, e) => OnMouseDown(e);
        _grid.MouseUp += (_, e) => OnMouseUp(e);
        _grid.MouseMove += (_, e) => OnMouseMove(e);
        _grid.MouseEnter += (_, e) => OnMouseEnter(e);
        _grid.MouseLeave += (_, e) => OnMouseLeave(e);
        _grid.MouseWheel += (_, e) => OnMouseWheel(e);
        _grid.GotFocus += (_, e) => OnGotFocus(e);
        _grid.LostFocus += (_, e) => OnLostFocus(e);

        Controls.Add(_grid);
        _columns.CollectionChanged += OnColumnsChanged;
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

    /// <summary>Gets or sets whether columns are inferred when <see cref="Columns"/> is empty.</summary>
    public bool AutoGenerateColumns
    {
        get => (bool)GetValue(AutoGenerateColumnsProperty)!;
        set => SetValue(AutoGenerateColumnsProperty, value);
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

    /// <summary>The declared columns, left to right.</summary>
    public IList<DataGridColumn> Columns => _columns;

    /// <summary>The number of rows in the native grid.</summary>
    public int ItemCount => _grid.Rows.Count;

    /// <summary>The number of columns in the native grid.</summary>
    public int ColumnCount => _grid.Columns.Count;

    /// <summary>Raised after the native grid reports a selection change.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Moves focus to the native grid.</summary>
    /// <returns><see langword="true"/> when the grid received focus.</returns>
    public new bool Focus() => Focusable && _grid.Focus();

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var border = BorderThickness;
        return new Size(
            240 + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            160 + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
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
        if (_grid.Bounds != inset)
        {
            _grid.Bounds = inset;
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
            DetachSource();
            _columns.CollectionChanged -= OnColumnsChanged;
            _grid.SelectionChanged -= OnSelectionChanged;
            if (Controls.Contains(_grid))
            {
                Controls.Remove(_grid);
            }

            _grid.Dispose();
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
        if (IsDisposed || _grid.IsDisposed)
        {
            return;
        }

        var theme = Theme.Current;
        _grid.BackgroundColor = XamlDrawing.ToOpaque(EffectiveBackground, theme.Surface);
        _grid.GridColor = theme.ControlBorder;
        _grid.DefaultCellStyle.BackColor = XamlDrawing.ToOpaque(EffectiveBackground, SystemColors.Window);
        _grid.DefaultCellStyle.ForeColor = EffectiveForeground;
        _grid.DefaultCellStyle.SelectionBackColor = theme.Selection;
        _grid.DefaultCellStyle.SelectionForeColor = theme.Foreground;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = theme.SurfaceAlt;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = theme.Foreground;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = theme.SurfaceAlt;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = theme.Foreground;
        _grid.ColumnHeadersHeight = Math.Max(20, theme.ControlHeight);
        _grid.RowTemplate.Height = Math.Max(16, theme.ControlHeight);
        _grid.Font = Font;
        _grid.Enabled = IsEnabled;
        Invalidate();
    }

    private void Rebuild()
    {
        if (IsDisposed)
        {
            return;
        }

        var items = new List<object?>();
        if (ItemsSource is { } source)
        {
            foreach (var item in source)
            {
                items.Add(item);
            }
        }

        _syncing = true;
        try
        {
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                _grid.Columns.Clear();

                var specs = ResolveColumns(items);
                foreach (var spec in specs)
                {
                    var column = new NativeColumn
                    {
                        HeaderText = spec.Header,
                        Width = spec.Width > 0 ? spec.Width : 120,
                        ReadOnly = true,
                        SortMode = DataGridViewColumnSortMode.NotSortable,
                    };
                    _grid.Columns.Add(column);
                }

                if (specs.Count > 0)
                {
                    foreach (var item in items)
                    {
                        AddRow(item, specs);
                    }
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }
        }
        finally
        {
            _syncing = false;
        }

        ApplySelection();
    }

    private void AddRow(object? item, List<ColumnSpec> specs)
    {
        var index = _grid.Rows.Add();
        var row = _grid.Rows[index];
        row.Tag = item;
        row.Height = Math.Max(16, Theme.Current.ControlHeight);
        for (var column = 0; column < specs.Count; column++)
        {
            row.Cells[column].Value = ResolveValue(item, specs[column].Path);
        }
    }

    private List<ColumnSpec> ResolveColumns(List<object?> items)
    {
        var specs = new List<ColumnSpec>();

        if (_columns.Count > 0)
        {
            foreach (var column in _columns)
            {
                var path = string.IsNullOrEmpty(column.Binding) ? column.Header : column.Binding;
                var header = string.IsNullOrEmpty(column.Header) ? path : column.Header;
                specs.Add(new ColumnSpec(header, path, column.Width));
            }

            return specs;
        }

        if (!AutoGenerateColumns)
        {
            return specs;
        }

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(item))
            {
                if (string.Equals(descriptor.Name, "Item", StringComparison.Ordinal))
                {
                    continue;
                }

                specs.Add(new ColumnSpec(descriptor.DisplayName, descriptor.Name, 120));
            }

            break;
        }

        return specs;
    }

    private static object? ResolveValue(object? item, string? path)
    {
        if (item is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(path))
        {
            return item;
        }

        try
        {
            var bindingPath = new BindingPath(path);
            return bindingPath.TryResolve(item, out var value, out _, out _) ? value : null;
        }
        catch (Exception)
        {
            // 显示路径写错（或自动生成的列碰到索引器）时留空，不要让整张表建不出来。
            return null;
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncing || IsDisposed)
        {
            return;
        }

        var index = -1;
        object? item = null;

        if (_grid.SelectedRows.Count > 0)
        {
            index = _grid.SelectedRows[0].Index;
        }
        else if (_grid.CurrentRow is { } row)
        {
            index = row.Index;
        }

        if (index >= 0 && index < _grid.Rows.Count)
        {
            item = _grid.Rows[index].Tag;
        }

        _syncing = true;
        try
        {
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
                index = FindRowByTag(item);
            }
            else if (SelectedIndex >= 0 && SelectedIndex < _grid.Rows.Count)
            {
                index = SelectedIndex;
            }

            _grid.ClearSelection();
            if (index >= 0 && index < _grid.Rows.Count)
            {
                var row = _grid.Rows[index];
                row.Selected = true;
                if (row.Cells.Count > 0)
                {
                    _grid.CurrentCell = row.Cells[0];
                }

                if (SelectedIndex != index)
                {
                    SetValue(SelectedIndexProperty, index);
                }

                if (!Equals(SelectedItem, row.Tag))
                {
                    SetValue(SelectedItemProperty, row.Tag);
                }
            }
            else
            {
                _grid.CurrentCell = null;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private int FindRowByTag(object item)
    {
        for (var index = 0; index < _grid.Rows.Count; index++)
        {
            if (ReferenceEquals(_grid.Rows[index].Tag, item))
            {
                return index;
            }
        }

        for (var index = 0; index < _grid.Rows.Count; index++)
        {
            if (Equals(_grid.Rows[index].Tag, item))
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
            _observed = observable;
            observable.CollectionChanged += OnCollectionChanged;
        }
    }

    private void DetachSource()
    {
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnCollectionChanged;
            _observed = null;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private static void OnItemsSourcePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is DataGrid grid && !grid.IsDisposed)
        {
            grid.AttachSource(e.NewValue as IEnumerable);
            grid.Rebuild();
        }
    }

    private static void OnStructurePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is DataGrid grid && !grid.IsDisposed)
        {
            grid.Rebuild();
        }
    }

    private static void OnSelectionPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is DataGrid grid && !grid.IsDisposed && !grid._syncing)
        {
            grid.ApplySelection();
        }
    }

    private static void OnChromePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is DataGrid grid && !grid.IsDisposed)
        {
            grid.ApplyNativeAppearance();
        }
    }

    private readonly record struct ColumnSpec(string Header, string Path, int Width);
}

/// <summary>One column of a <see cref="DataGrid"/>.</summary>
public class DataGridColumn
{
    /// <summary>The column header text.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>The property path read for each cell; empty falls back to <see cref="Header"/>.</summary>
    public string Binding { get; set; } = string.Empty;

    /// <summary>The column width in pixels; zero or less uses 120.</summary>
    public int Width { get; set; } = 120;
}
