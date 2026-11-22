using System.Collections.ObjectModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using XamlColumnDefinition = VeloxDev.WinForms.Xaml.Controls.ColumnDefinition;
using XamlGridLength = VeloxDev.WinForms.Xaml.Controls.GridLength;
using XamlRowDefinition = VeloxDev.WinForms.Xaml.Controls.RowDefinition;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>Arranges children in rows and columns whose tracks are pixel, auto or star sized.</summary>
/// <remarks>
/// <para>
/// Tracks are declared through <see cref="RowDefinitions"/> and <see cref="ColumnDefinitions"/>; a grid with no
/// definitions behaves as one star row and one star column. Each child picks its cell through the attached
/// <see cref="GetRow"/>, <see cref="GetColumn"/>, <see cref="GetRowSpan"/> and <see cref="GetColumnSpan"/> values.
/// </para>
/// <para>
/// Auto tracks take the largest desired size of the children that touch them; a spanning child contributes only to
/// the first auto track in its span. Star tracks then divide the room left by the pixel and auto tracks by weight.
/// </para>
/// </remarks>
public class Grid : Panel
{
    /// <summary>The attached row index.</summary>
    public static readonly WinformsProperty<int> RowProperty =
        WinformsProperty.RegisterAttached<Grid, int>("Row", 0, affectsMeasure: true);

    /// <summary>The attached column index.</summary>
    public static readonly WinformsProperty<int> ColumnProperty =
        WinformsProperty.RegisterAttached<Grid, int>("Column", 0, affectsMeasure: true);

    /// <summary>The attached row span; values below one become one.</summary>
    public static readonly WinformsProperty<int> RowSpanProperty =
        WinformsProperty.RegisterAttached<Grid, int>("RowSpan", 1, affectsMeasure: true);

    /// <summary>The attached column span; values below one become one.</summary>
    public static readonly WinformsProperty<int> ColumnSpanProperty =
        WinformsProperty.RegisterAttached<Grid, int>("ColumnSpan", 1, affectsMeasure: true);

    /// <summary>Creates an empty grid.</summary>
    public Grid()
    {
        RowDefinitions = new TrackCollection<XamlRowDefinition>(this);
        ColumnDefinitions = new TrackCollection<XamlColumnDefinition>(this);
    }

    /// <summary>The row definitions; a grid with none behaves as one star row.</summary>
    public IList<XamlRowDefinition> RowDefinitions { get; }

    /// <summary>The column definitions; a grid with none behaves as one star column.</summary>
    public IList<XamlColumnDefinition> ColumnDefinitions { get; }

    /// <summary>Reads the zero-based row of <paramref name="element"/>.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The row index; zero when the element never set one.</returns>
    public static int GetRow(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return RowProperty.GetValue(element);
    }

    /// <summary>Writes the zero-based row of <paramref name="element"/>.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The row index; negative values are treated as zero.</param>
    public static void SetRow(VisualElement element, int value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        RowProperty.SetValue(element, Math.Max(0, value));
    }

    /// <summary>Reads the zero-based column of <paramref name="element"/>.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The column index; zero when the element never set one.</returns>
    public static int GetColumn(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return ColumnProperty.GetValue(element);
    }

    /// <summary>Writes the zero-based column of <paramref name="element"/>.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The column index; negative values are treated as zero.</param>
    public static void SetColumn(VisualElement element, int value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        ColumnProperty.SetValue(element, Math.Max(0, value));
    }

    /// <summary>Reads how many rows <paramref name="element"/> spans.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The span; at least one.</returns>
    public static int GetRowSpan(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return Math.Max(1, RowSpanProperty.GetValue(element));
    }

    /// <summary>Writes how many rows <paramref name="element"/> spans.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The span; values below one are treated as one.</param>
    public static void SetRowSpan(VisualElement element, int value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        RowSpanProperty.SetValue(element, Math.Max(1, value));
    }

    /// <summary>Reads how many columns <paramref name="element"/> spans.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The span; at least one.</returns>
    public static int GetColumnSpan(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return Math.Max(1, ColumnSpanProperty.GetValue(element));
    }

    /// <summary>Writes how many columns <paramref name="element"/> spans.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The span; values below one are treated as one.</param>
    public static void SetColumnSpan(VisualElement element, int value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        ColumnSpanProperty.SetValue(element, Math.Max(1, value));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var content = new Size(
            Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical)));
        var (widths, heights) = ResolveTracks(content);
        return new Size(
            (int)Math.Ceiling(Sum(widths) + padding.Horizontal),
            (int)Math.Ceiling(Sum(heights) + padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        var content = new Size(
            Math.Max(0, finalSize.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, finalSize.Height - (int)Math.Ceiling(padding.Vertical)));
        var (widths, heights) = ResolveTracks(content);
        var columnOffsets = Offsets(widths, padding.Left);
        var rowOffsets = Offsets(heights, padding.Top);

        foreach (var child in ArrangedChildren())
        {
            var placement = PlacementOf(child);
            var column = XamlMath.Clamp(placement.Column, 0, widths.Length - 1);
            var row = XamlMath.Clamp(placement.Row, 0, heights.Length - 1);
            var columnSpan = XamlMath.Clamp(placement.ColumnSpan, 1, widths.Length - column);
            var rowSpan = XamlMath.Clamp(placement.RowSpan, 1, heights.Length - row);

            var slot = new RectangleF(
                columnOffsets[column],
                rowOffsets[row],
                (float)SpanSize(widths, column, columnSpan),
                (float)SpanSize(heights, row, rowSpan));
            child.Arrange(slot);
        }
    }

    // 测一次子项得到自然尺寸，再按定义分到像素/自动/星号三种轨道。
    private (double[] Widths, double[] Heights) ResolveTracks(Size content)
    {
        var columnDefinitions = ColumnDefinitions.Count > 0
            ? ColumnDefinitions.ToArray()
            : [new XamlColumnDefinition()];
        var rowDefinitions = RowDefinitions.Count > 0
            ? RowDefinitions.ToArray()
            : [new XamlRowDefinition()];

        var widths = new double[columnDefinitions.Length];
        var heights = new double[rowDefinitions.Length];

        for (var index = 0; index < columnDefinitions.Length; index++)
        {
            var width = columnDefinitions[index].Width;
            widths[index] = width.IsAbsolute ? Math.Ceiling(Math.Max(0d, width.Value)) : 0d;
        }

        for (var index = 0; index < rowDefinitions.Length; index++)
        {
            var height = rowDefinitions[index].Height;
            heights[index] = height.IsAbsolute ? Math.Ceiling(Math.Max(0d, height.Value)) : 0d;
        }

        foreach (var child in ArrangedChildren())
        {
            var measured = child.Measure(content);
            var placement = PlacementOf(child);
            var column = XamlMath.Clamp(placement.Column, 0, widths.Length - 1);
            var row = XamlMath.Clamp(placement.Row, 0, heights.Length - 1);
            var columnSpan = XamlMath.Clamp(placement.ColumnSpan, 1, widths.Length - column);
            var rowSpan = XamlMath.Clamp(placement.RowSpan, 1, heights.Length - row);

            var autoColumn = FirstAutoColumn(columnDefinitions, column, columnSpan);
            if (autoColumn >= 0)
            {
                widths[autoColumn] = Math.Max(widths[autoColumn], measured.Width);
            }

            var autoRow = FirstAutoRow(rowDefinitions, row, rowSpan);
            if (autoRow >= 0)
            {
                heights[autoRow] = Math.Max(heights[autoRow], measured.Height);
            }
        }

        DistributeStars(
            columnDefinitions.Select(static definition => definition.Width).ToArray(),
            widths,
            content.Width);
        DistributeStars(
            rowDefinitions.Select(static definition => definition.Height).ToArray(),
            heights,
            content.Height);
        return (widths, heights);
    }

    // 星号轨道平分扣除像素与自动轨道后的剩余；最后一格吃掉取整误差，保证总宽等于可用宽。
    private static void DistributeStars(XamlGridLength[] lengths, double[] sizes, double available)
    {
        var totalWeight = 0d;
        var lastStar = -1;
        var fixedSize = 0d;
        for (var index = 0; index < lengths.Length; index++)
        {
            if (lengths[index].IsStar)
            {
                totalWeight += Math.Max(0d, lengths[index].Value);
                lastStar = index;
            }
            else
            {
                fixedSize += sizes[index];
            }
        }

        if (totalWeight <= 0d || lastStar < 0 || available <= 0d)
        {
            return;
        }

        var remaining = Math.Max(0d, available - fixedSize);
        var assigned = 0d;
        for (var index = 0; index < lengths.Length; index++)
        {
            if (!lengths[index].IsStar)
            {
                continue;
            }

            var share = index == lastStar
                ? remaining - assigned
                : Math.Floor(remaining * (Math.Max(0d, lengths[index].Value) / totalWeight));
            sizes[index] = Math.Max(0d, share);
            assigned += share;
        }
    }

    private static int FirstAutoColumn(XamlColumnDefinition[] definitions, int start, int span)
    {
        for (var index = start; index < start + span; index++)
        {
            if (definitions[index].Width.IsAuto)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FirstAutoRow(XamlRowDefinition[] definitions, int start, int span)
    {
        for (var index = start; index < start + span; index++)
        {
            if (definitions[index].Height.IsAuto)
            {
                return index;
            }
        }

        return -1;
    }

    // 折叠的子项既不测量也不占格；Hidden 仍然参与布局，只是不绘制。
    private IEnumerable<VisualElement> ArrangedChildren()
    {
        foreach (var child in Children)
        {
            if (child is VisualElement element && element.Visibility != XamlVisibility.Collapsed)
            {
                yield return element;
            }
        }
    }

    private static double SpanSize(double[] sizes, int start, int span)
    {
        var total = 0d;
        for (var index = start; index < start + span; index++)
        {
            total += sizes[index];
        }

        return total;
    }

    private static double Sum(double[] sizes)
    {
        var total = 0d;
        foreach (var size in sizes)
        {
            total += size;
        }

        return total;
    }

    private static float[] Offsets(double[] sizes, float origin)
    {
        var offsets = new float[sizes.Length];
        var position = origin;
        for (var index = 0; index < sizes.Length; index++)
        {
            offsets[index] = position;
            position += (float)sizes[index];
        }

        return offsets;
    }

    private static (int Row, int Column, int RowSpan, int ColumnSpan) PlacementOf(VisualElement element)
        => (GetRow(element), GetColumn(element), GetRowSpan(element), GetColumnSpan(element));

    // 定义集合被增删时让整个网格重新测量；修改已有定义的 Width/Height 不会触发通知。
    private sealed class TrackCollection<T>(Grid owner) : Collection<T>
    {
        protected override void InsertItem(int index, T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            base.InsertItem(index, item);
            owner.InvalidateMeasure();
        }

        protected override void SetItem(int index, T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            base.SetItem(index, item);
            owner.InvalidateMeasure();
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            owner.InvalidateMeasure();
        }

        protected override void ClearItems()
        {
            base.ClearItems();
            owner.InvalidateMeasure();
        }
    }
}
