using System.Drawing;
using System.Windows.Forms;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>Places children in a row or column and wraps to a new line when the current one is full.</summary>
/// <remarks>
/// <see cref="ItemWidth"/> and <see cref="ItemHeight"/> give every child the same slot size when positive; zero
/// keeps the child's measured size. <see cref="Spacing"/> is applied both between items of a line and between lines.
/// A vertical <see cref="Orientation"/> wraps top to bottom instead.
/// </remarks>
public class WrapPanel : Panel
{
    private Orientation _orientation = Orientation.Horizontal;
    private int _itemWidth;
    private int _itemHeight;
    private int _spacing;

    /// <summary>The wrapping direction: horizontally by default.</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value)
            {
                return;
            }

            _orientation = value;
            InvalidateMeasure();
        }
    }

    /// <summary>A fixed slot width for every child, in pixels; zero keeps the measured width.</summary>
    public int ItemWidth
    {
        get => _itemWidth;
        set
        {
            if (_itemWidth == value)
            {
                return;
            }

            _itemWidth = Math.Max(0, value);
            InvalidateMeasure();
        }
    }

    /// <summary>A fixed slot height for every child, in pixels; zero keeps the measured height.</summary>
    public int ItemHeight
    {
        get => _itemHeight;
        set
        {
            if (_itemHeight == value)
            {
                return;
            }

            _itemHeight = Math.Max(0, value);
            InvalidateMeasure();
        }
    }

    /// <summary>The gap between adjacent items and between lines, in pixels.</summary>
    public int Spacing
    {
        get => _spacing;
        set
        {
            if (_spacing == value)
            {
                return;
            }

            _spacing = Math.Max(0, value);
            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var width = availableSize.Width > 0
            ? Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal))
            : 0;
        var height = availableSize.Height > 0
            ? Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical))
            : 0;
        var lines = ArrangeLines(width, height, arrange: false, PointF.Empty);
        return new Size(
            lines.Width + (int)Math.Ceiling(padding.Horizontal),
            lines.Height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        ArrangeLines(
            Math.Max(0, finalSize.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, finalSize.Height - (int)Math.Ceiling(padding.Vertical)),
            arrange: true,
            new PointF((int)Math.Ceiling(padding.Left), (int)Math.Ceiling(padding.Top)));
    }

    // 同一条流程既用于测量也用于摆放：测量时只统计行尺寸，摆放时把槽位交给子项。
    private Size ArrangeLines(int width, int height, bool arrange, PointF origin)
    {
        var horizontal = _orientation == Orientation.Horizontal;
        var mainLimit = horizontal
            ? width > 0 ? width : int.MaxValue
            : height > 0 ? height : int.MaxValue;
        var fixedMain = horizontal ? _itemWidth : _itemHeight;
        var fixedCross = horizontal ? _itemHeight : _itemWidth;
        var spacing = Math.Max(0, _spacing);

        var cursor = 0;
        var lineExtent = 0;
        var crossOffset = 0;
        var lineCross = 0;
        var totalMain = 0;
        var hasItems = false;

        foreach (var child in ArrangedChildren())
        {
            var measured = child.Measure(new Size(width, height));
            var main = fixedMain > 0 ? fixedMain : horizontal ? measured.Width : measured.Height;
            var cross = fixedCross > 0 ? fixedCross : horizontal ? measured.Height : measured.Width;

            if (cursor > 0 && cursor + main > mainLimit)
            {
                totalMain = Math.Max(totalMain, lineExtent);
                crossOffset += lineCross + spacing;
                cursor = 0;
                lineExtent = 0;
                lineCross = 0;
            }

            if (arrange)
            {
                var slot = horizontal
                    ? new RectangleF(origin.X + cursor, origin.Y + crossOffset, main, cross)
                    : new RectangleF(origin.X + crossOffset, origin.Y + cursor, cross, main);
                child.Arrange(slot);
            }

            cursor += main + spacing;
            lineExtent = Math.Max(lineExtent, cursor - spacing);
            lineCross = Math.Max(lineCross, cross);
            hasItems = true;
        }

        if (!hasItems)
        {
            return Size.Empty;
        }

        totalMain = Math.Max(totalMain, lineExtent);
        var totalCross = crossOffset + lineCross;
        return horizontal
            ? new Size(totalMain, totalCross)
            : new Size(totalCross, totalMain);
    }

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
}
