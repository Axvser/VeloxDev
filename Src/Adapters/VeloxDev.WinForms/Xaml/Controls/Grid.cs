using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Arranges children in rows and columns, like a WPF or Avalonia grid.
/// </summary>
/// <remarks>
/// <para>
/// Declare <see cref="RowDefinitions"/> and <see cref="ColumnDefinitions"/> with pixel, auto or star lengths.
/// A grid with no definitions behaves as one star row and one star column. Each child picks its cell through the
/// attached <see cref="RowProperty"/>, <see cref="ColumnProperty"/>, <see cref="RowSpanProperty"/> and
/// <see cref="ColumnSpanProperty"/> values.
/// </para>
/// <para>
/// Spanning children contribute their measured size to the first auto row or column they touch; star rows and
/// columns then share what is left. The layout is intentionally the common case, not a full WPF measure pass.
/// </para>
/// </remarks>
public class Grid : Control
{
    /// <summary>The zero-based row a child occupies.</summary>
    public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached(
        "Row",
        typeof(int),
        typeof(Grid),
        new PropertyMetadata(0, OnCellPropertyChanged));

    /// <summary>The zero-based column a child occupies.</summary>
    public static readonly DependencyProperty ColumnProperty = DependencyProperty.RegisterAttached(
        "Column",
        typeof(int),
        typeof(Grid),
        new PropertyMetadata(0, OnCellPropertyChanged));

    /// <summary>The number of rows a child spans; at least one.</summary>
    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan",
        typeof(int),
        typeof(Grid),
        new PropertyMetadata(1, OnCellPropertyChanged));

    /// <summary>The number of columns a child spans; at least one.</summary>
    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan",
        typeof(int),
        typeof(Grid),
        new PropertyMetadata(1, OnCellPropertyChanged));

    /// <summary>Creates the grid with double buffering.</summary>
    public Grid()
    {
        DoubleBuffered = true;
        ((DefinitionCollection<RowDefinition>)RowDefinitions).Owner = this;
        ((DefinitionCollection<ColumnDefinition>)ColumnDefinitions).Owner = this;
    }

    /// <summary>The row definitions; a grid with none behaves as one star row.</summary>
    public IList<RowDefinition> RowDefinitions { get; } = new DefinitionCollection<RowDefinition>();

    /// <summary>The column definitions; a grid with none behaves as one star column.</summary>
    public IList<ColumnDefinition> ColumnDefinitions { get; } = new DefinitionCollection<ColumnDefinition>();

    /// <summary>Reads the row index of <paramref name="element"/>.</summary>
    public static int GetRow(System.Windows.Forms.Control element)
        => (int)(RowProperty.GetValue(element) ?? 0);

    /// <summary>Writes the row index of <paramref name="element"/>.</summary>
    public static void SetRow(System.Windows.Forms.Control element, int value)
        => RowProperty.SetValue(element, value);

    /// <summary>Reads the column index of <paramref name="element"/>.</summary>
    public static int GetColumn(System.Windows.Forms.Control element)
        => (int)(ColumnProperty.GetValue(element) ?? 0);

    /// <summary>Writes the column index of <paramref name="element"/>.</summary>
    public static void SetColumn(System.Windows.Forms.Control element, int value)
        => ColumnProperty.SetValue(element, value);

    /// <summary>Reads how many rows <paramref name="element"/> spans.</summary>
    public static int GetRowSpan(System.Windows.Forms.Control element)
        => Math.Max(1, (int)(RowSpanProperty.GetValue(element) ?? 1));

    /// <summary>Writes how many rows <paramref name="element"/> spans.</summary>
    public static void SetRowSpan(System.Windows.Forms.Control element, int value)
        => RowSpanProperty.SetValue(element, Math.Max(1, value));

    /// <summary>Reads how many columns <paramref name="element"/> spans.</summary>
    public static int GetColumnSpan(System.Windows.Forms.Control element)
        => Math.Max(1, (int)(ColumnSpanProperty.GetValue(element) ?? 1));

    /// <summary>Writes how many columns <paramref name="element"/> spans.</summary>
    public static void SetColumnSpan(System.Windows.Forms.Control element, int value)
        => ColumnSpanProperty.SetValue(element, Math.Max(1, value));

    protected internal override Size MeasureCore(Size availableSize)
    {
        var content = XamlLayout.ContentBounds(this).Size;
        var (columns, rows) = ResolveTracks(content);
        return new Size(
            columns.Sum() + (int)Math.Ceiling(Padding.Horizontal),
            rows.Sum() + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var content = XamlLayout.ContentBounds(this);
        var (columns, rows) = ResolveTracks(content.Size);
        var columnOffsets = Offsets(columns, content.Left);
        var rowOffsets = Offsets(rows, content.Top);

        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                XamlLayout.Hide(child);
                continue;
            }

            var column = Math.Clamp(GetColumn(child), 0, Math.Max(0, columns.Length - 1));
            var row = Math.Clamp(GetRow(child), 0, Math.Max(0, rows.Length - 1));
            var columnSpan = Math.Clamp(GetColumnSpan(child), 1, columns.Length - column);
            var rowSpan = Math.Clamp(GetRowSpan(child), 1, rows.Length - row);

            var width = 0;
            for (var index = column; index < column + columnSpan; index++)
            {
                width += columns[index];
            }

            var height = 0;
            for (var index = row; index < row + rowSpan; index++)
            {
                height += rows[index];
            }

            XamlLayout.Arrange(child, new Rectangle(columnOffsets[column], rowOffsets[row], width, height));
        }
    }

    private (int[] Columns, int[] Rows) ResolveTracks(Size available)
    {
        object[] columnDefinitions = ColumnDefinitions.Count > 0
            ? ColumnDefinitions.Cast<object>().ToArray()
            : [new ColumnDefinition()];
        object[] rowDefinitions = RowDefinitions.Count > 0
            ? RowDefinitions.Cast<object>().ToArray()
            : [new RowDefinition()];

        var columns = new int[columnDefinitions.Length];
        var rows = new int[rowDefinitions.Length];

        FillAutoSizes(columnDefinitions, columns, available, isColumn: true);
        FillAutoSizes(rowDefinitions, rows, available, isColumn: false);

        var fixedWidth = columns.Sum();
        var fixedHeight = rows.Sum();
        var totalStarColumns = columnDefinitions
            .OfType<ColumnDefinition>()
            .Where(static definition => definition.Width.IsStar)
            .Sum(static definition => Math.Max(0d, definition.Width.Value));
        var totalStarRows = rowDefinitions
            .OfType<RowDefinition>()
            .Where(static definition => definition.Height.IsStar)
            .Sum(static definition => Math.Max(0d, definition.Height.Value));

        if (totalStarColumns > 0d)
        {
            var remaining = Math.Max(0, available.Width - fixedWidth);
            var assigned = 0;
            for (var index = 0; index < columns.Length; index++)
            {
                if (columnDefinitions[index] is not ColumnDefinition definition || !definition.Width.IsStar)
                {
                    continue;
                }

                var share = index == LastStar(columnDefinitions, isColumn: true)
                    ? Math.Max(0, remaining - assigned)
                    : (int)Math.Floor(remaining * (Math.Max(0d, definition.Width.Value) / totalStarColumns));
                columns[index] = share;
                assigned += share;
            }
        }

        if (totalStarRows > 0d)
        {
            var remaining = Math.Max(0, available.Height - fixedHeight);
            var assigned = 0;
            for (var index = 0; index < rows.Length; index++)
            {
                if (rowDefinitions[index] is not RowDefinition definition || !definition.Height.IsStar)
                {
                    continue;
                }

                var share = index == LastStar(rowDefinitions, isColumn: false)
                    ? Math.Max(0, remaining - assigned)
                    : (int)Math.Floor(remaining * (Math.Max(0d, definition.Height.Value) / totalStarRows));
                rows[index] = share;
                assigned += share;
            }
        }

        return (columns, rows);
    }

    private int[] FillAutoSizes(
        IReadOnlyList<object> definitions,
        int[] sizes,
        Size available,
        bool isColumn)
    {
        for (var index = 0; index < definitions.Count; index++)
        {
            var isAuto = isColumn
                ? definitions[index] is ColumnDefinition { Width.IsAuto: true }
                : definitions[index] is RowDefinition { Height.IsAuto: true };
            var absolute = isColumn
                ? definitions[index] is ColumnDefinition { Width.IsAbsolute: true } column ? (int)Math.Ceiling(column.Width.Value) : 0
                : definitions[index] is RowDefinition { Height.IsAbsolute: true } row ? (int)Math.Ceiling(row.Height.Value) : 0;
            sizes[index] = isAuto ? 0 : Math.Max(0, absolute);
        }

        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                continue;
            }

            var measured = XamlLayout.Measure(child, available);
            var index = isColumn
                ? Math.Clamp(GetColumn(child), 0, sizes.Length - 1)
                : Math.Clamp(GetRow(child), 0, sizes.Length - 1);

            var isAuto = isColumn
                ? definitions[index] is ColumnDefinition { Width.IsAuto: true }
                : definitions[index] is RowDefinition { Height.IsAuto: true };
            if (isAuto)
            {
                sizes[index] = Math.Max(sizes[index], isColumn ? measured.Width : measured.Height);
            }
        }

        return sizes;
    }

    private static int LastStar(IReadOnlyList<object> definitions, bool isColumn)
    {
        for (var index = definitions.Count - 1; index >= 0; index--)
        {
            var isStar = isColumn
                ? definitions[index] is ColumnDefinition { Width.IsStar: true }
                : definitions[index] is RowDefinition { Height.IsStar: true };
            if (isStar)
            {
                return index;
            }
        }

        return -1;
    }

    private static int[] Offsets(int[] sizes, int origin)
    {
        var offsets = new int[sizes.Length];
        var position = origin;
        for (var index = 0; index < sizes.Length; index++)
        {
            offsets[index] = position;
            position += sizes[index];
        }

        return offsets;
    }

    private static void OnCellPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Parent?.PerformLayout();
        }
    }

    private static void OnLayoutPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.PerformLayout();
        }
    }

    private sealed class DefinitionCollection<T> : Collection<T>, IList<T>
    {
        protected override void InsertItem(int index, T item)
        {
            base.InsertItem(index, item);
            Owner?.PerformLayout();
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            Owner?.PerformLayout();
        }

        protected override void SetItem(int index, T item)
        {
            base.SetItem(index, item);
            Owner?.PerformLayout();
        }

        protected override void ClearItems()
        {
            base.ClearItems();
            Owner?.PerformLayout();
        }

        internal Grid? Owner { get; set; }
    }
}
