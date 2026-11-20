using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Places children left to right, wrapping to a new line when the current one is full.
/// </summary>
/// <remarks>
/// <see cref="ItemWidth"/> and <see cref="ItemHeight"/> give every child the same slot size when positive; zero
/// means each child keeps its measured size. Vertical orientation wraps top to bottom instead.
/// </remarks>
public class WrapPanel : Control
{
    /// <summary>The wrapping direction: horizontally by default.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(WrapPanel),
        new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    /// <summary>A fixed slot width for every child, in pixels; zero keeps the measured width.</summary>
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(int),
        typeof(WrapPanel),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>A fixed slot height for every child, in pixels; zero keeps the measured height.</summary>
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(int),
        typeof(WrapPanel),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>Creates the panel with double buffering.</summary>
    public WrapPanel()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the wrapping direction.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets the fixed slot width in pixels; zero keeps the measured width.</summary>
    public int ItemWidth
    {
        get => (int)GetValue(ItemWidthProperty)!;
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>Gets or sets the fixed slot height in pixels; zero keeps the measured height.</summary>
    public int ItemHeight
    {
        get => (int)GetValue(ItemHeightProperty)!;
        set => SetValue(ItemHeightProperty, value);
    }

    protected internal override Size MeasureCore(Size availableSize)
    {
        var width = availableSize.Width > 0
            ? Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal))
            : int.MaxValue;
        var height = availableSize.Height > 0
            ? Math.Max(0, availableSize.Height - (int)Math.Ceiling(Padding.Vertical))
            : int.MaxValue;
        var lines = ArrangeChildren(width, height, arrange: false);
        return new Size(
            lines.Width + (int)Math.Ceiling(Padding.Horizontal),
            lines.Height + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var content = XamlLayout.ContentBounds(this);
        ArrangeChildren(content.Width, content.Height, arrange: true, origin: content.Location);
    }

    private Size ArrangeChildren(int width, int height, bool arrange, Point origin = default)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var fixedMain = horizontal ? ItemWidth : ItemHeight;
        var fixedCross = horizontal ? ItemHeight : ItemWidth;
        var lineMain = 0;
        var lineCross = 0;
        var totalMain = 0;
        var totalCross = 0;
        var positionMain = 0;

        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                XamlLayout.Hide(child);
                continue;
            }

            var measured = XamlLayout.Measure(child, new Size(width, height));
            var main = fixedMain > 0 ? fixedMain : horizontal ? measured.Width : measured.Height;
            var cross = fixedCross > 0 ? fixedCross : horizontal ? measured.Height : measured.Width;
            var limit = horizontal ? width : height;

            if (positionMain > 0 && positionMain + main > limit)
            {
                totalMain = Math.Max(totalMain, lineMain);
                totalCross += lineCross;
                positionMain = 0;
                lineMain = 0;
                lineCross = 0;
            }

            if (arrange)
            {
                var slot = horizontal
                    ? new Rectangle(origin.X + positionMain, origin.Y + totalCross, main, cross)
                    : new Rectangle(origin.X + totalCross, origin.Y + positionMain, cross, main);
                XamlLayout.Arrange(child, slot);
            }

            positionMain += main;
            lineMain = Math.Max(lineMain, positionMain);
            lineCross = Math.Max(lineCross, cross);
        }

        return new Size(
            horizontal ? Math.Max(totalMain, lineMain) : totalCross + lineCross,
            horizontal ? totalCross + lineCross : Math.Max(totalMain, lineMain));
    }

    private static void OnLayoutPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.PerformLayout();
        }
    }
}
