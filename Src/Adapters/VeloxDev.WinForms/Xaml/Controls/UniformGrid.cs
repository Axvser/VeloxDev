using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Arranges children into equal-sized cells, like a WPF or Avalonia uniform grid.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Columns"/> and <see cref="Rows"/> fix the cell grid; leaving both at zero derives a near-square grid from
/// the child count. <see cref="FirstColumn"/> skips leading cells in the first row.
/// </para>
/// <para>
/// Every cell has the same size. A child keeps its <see cref="Control.HorizontalAlignment"/> and
/// <see cref="Control.VerticalAlignment"/> inside its cell.
/// </para>
/// </remarks>
public class UniformGrid : Control
{
    /// <summary>The number of rows; zero derives the row count from <see cref="Columns"/> or the child count.</summary>
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows),
        typeof(int),
        typeof(UniformGrid),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>The number of columns; zero derives the column count from <see cref="Rows"/> or the child count.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(int),
        typeof(UniformGrid),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>The number of leading cells left empty in the first row; zero by default.</summary>
    public static readonly DependencyProperty FirstColumnProperty = DependencyProperty.Register(
        nameof(FirstColumn),
        typeof(int),
        typeof(UniformGrid),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>Creates the panel with double buffering.</summary>
    public UniformGrid()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the number of rows; zero derives it.</summary>
    public int Rows
    {
        get => (int)GetValue(RowsProperty)!;
        set => SetValue(RowsProperty, value);
    }

    /// <summary>Gets or sets the number of columns; zero derives it.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty)!;
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Gets or sets the number of leading cells skipped in the first row.</summary>
    public int FirstColumn
    {
        get => (int)GetValue(FirstColumnProperty)!;
        set => SetValue(FirstColumnProperty, value);
    }

    /// <inheritdoc />
    protected internal override Size MeasureCore(Size availableSize)
    {
        var padding = Padding;
        var contentWidth = availableSize.Width > 0
            ? Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal))
            : 0;
        var contentHeight = availableSize.Height > 0
            ? Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical))
            : 0;

        var count = CountOccupyingChildren();
        var (columns, rows) = ResolveTracks(count + Math.Max(0, FirstColumn));

        var cellWidth = 0;
        var cellHeight = 0;
        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                continue;
            }

            var measured = XamlLayout.Measure(child, new Size(contentWidth, contentHeight));
            cellWidth = Math.Max(cellWidth, measured.Width);
            cellHeight = Math.Max(cellHeight, measured.Height);
        }

        return ConstrainSize(
            new Size(
                (columns * cellWidth) + (int)Math.Ceiling(padding.Horizontal),
                (rows * cellHeight) + (int)Math.Ceiling(padding.Vertical)),
            availableSize);
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var content = XamlLayout.ContentBounds(this);
        var count = CountOccupyingChildren();
        var (columns, rows) = ResolveTracks(count + Math.Max(0, FirstColumn));
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        var cellWidth = Math.Max(0, content.Width / columns);
        var cellHeight = Math.Max(0, content.Height / rows);
        var slotIndex = Math.Max(0, FirstColumn);
        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                XamlLayout.Hide(child);
                continue;
            }

            var column = slotIndex % columns;
            var row = slotIndex / columns;
            var x = content.Left + (column * cellWidth);
            var y = content.Top + (row * cellHeight);
            var width = column == columns - 1 ? Math.Max(0, content.Right - x) : cellWidth;
            var height = row == rows - 1 ? Math.Max(0, content.Bottom - y) : cellHeight;

            XamlLayout.Arrange(child, new Rectangle(x, y, width, height));
            slotIndex++;
        }
    }

    private int CountOccupyingChildren()
    {
        var count = 0;
        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (XamlLayout.OccupiesSpace(child))
            {
                count++;
            }
        }

        return count;
    }

    // 显式的 Rows/Columns 优先；都没写时按子项数推一个接近正方形的网格。
    private (int Columns, int Rows) ResolveTracks(int count)
    {
        count = Math.Max(0, count);

        int columns;
        if (Columns > 0)
        {
            columns = Columns;
        }
        else if (Rows > 0)
        {
            columns = Math.Max(1, (int)Math.Ceiling(count / (double)Rows));
        }
        else
        {
            columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        }

        var rows = Rows > 0
            ? Rows
            : Math.Max(1, (int)Math.Ceiling(count / (double)columns));

        return (Math.Max(1, columns), Math.Max(1, rows));
    }

    private static void OnLayoutPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.PerformLayout();
        }
    }
}
