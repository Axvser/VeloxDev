using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A plain container: a <see cref="Control"/> that paints its children with double buffering and no extra behavior.
/// </summary>
/// <remarks>Use it where a XAML tree needs a grouping element without a native WinForms counterpart.</remarks>
public class Panel : Control
{
    /// <summary>Creates the container and enables double buffering.</summary>
    public Panel()
    {
        DoubleBuffered = true;
    }
}

/// <summary>
/// A container that enables <see cref="System.Windows.Forms.ScrollableControl.AutoScroll"/> by default.
/// </summary>
/// <remarks>
/// <see cref="Control"/> already derives from <see cref="System.Windows.Forms.ScrollableControl"/>, so scrolling is
/// available on every base control; this type only turns it on for the containers that are meant to scroll.
/// </remarks>
public class ScrollableControl : Control
{
    /// <summary>Creates the container with automatic scrolling enabled.</summary>
    public ScrollableControl()
    {
        AutoScroll = true;
    }
}

/// <summary>
/// An absolutely positioned drawing surface.
/// </summary>
/// <remarks>
/// <para>
/// Children take their position from the attached <see cref="LeftProperty"/> and <see cref="TopProperty"/> values;
/// giving both left and right (or top and bottom) stretches the child between the opposite edges. A child with no
/// attached coordinate keeps its native <see cref="System.Windows.Forms.Control.Location"/>, so code that positions
/// controls directly still works.
/// </para>
/// <para>This type enables the double-buffered, resize-redrawing paint style that drawing surfaces usually want.</para>
/// </remarks>
public class Canvas : Control
{
    private const int Unset = int.MinValue;

    /// <summary>The distance from the canvas's left edge, in pixels.</summary>
    public static readonly DependencyProperty LeftProperty = DependencyProperty.RegisterAttached(
        "Left",
        typeof(int),
        typeof(Canvas),
        new PropertyMetadata(Unset, OnPositionPropertyChanged));

    /// <summary>The distance from the canvas's top edge, in pixels.</summary>
    public static readonly DependencyProperty TopProperty = DependencyProperty.RegisterAttached(
        "Top",
        typeof(int),
        typeof(Canvas),
        new PropertyMetadata(Unset, OnPositionPropertyChanged));

    /// <summary>The distance from the canvas's right edge, in pixels.</summary>
    public static readonly DependencyProperty RightProperty = DependencyProperty.RegisterAttached(
        "Right",
        typeof(int),
        typeof(Canvas),
        new PropertyMetadata(Unset, OnPositionPropertyChanged));

    /// <summary>The distance from the canvas's bottom edge, in pixels.</summary>
    public static readonly DependencyProperty BottomProperty = DependencyProperty.RegisterAttached(
        "Bottom",
        typeof(int),
        typeof(Canvas),
        new PropertyMetadata(Unset, OnPositionPropertyChanged));

    /// <summary>Creates the canvas with double buffering and resize redraw enabled.</summary>
    public Canvas()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>Reads the left coordinate of <paramref name="element"/>; <see cref="int.MinValue"/> means unset.</summary>
    public static int GetLeft(System.Windows.Forms.Control element)
        => (int)(LeftProperty.GetValue(element) ?? Unset);

    /// <summary>Writes the left coordinate of <paramref name="element"/>.</summary>
    public static void SetLeft(System.Windows.Forms.Control element, int value)
        => LeftProperty.SetValue(element, value);

    /// <summary>Reads the top coordinate of <paramref name="element"/>; <see cref="int.MinValue"/> means unset.</summary>
    public static int GetTop(System.Windows.Forms.Control element)
        => (int)(TopProperty.GetValue(element) ?? Unset);

    /// <summary>Writes the top coordinate of <paramref name="element"/>.</summary>
    public static void SetTop(System.Windows.Forms.Control element, int value)
        => TopProperty.SetValue(element, value);

    /// <summary>Reads the right coordinate of <paramref name="element"/>; <see cref="int.MinValue"/> means unset.</summary>
    public static int GetRight(System.Windows.Forms.Control element)
        => (int)(RightProperty.GetValue(element) ?? Unset);

    /// <summary>Writes the right coordinate of <paramref name="element"/>.</summary>
    public static void SetRight(System.Windows.Forms.Control element, int value)
        => RightProperty.SetValue(element, value);

    /// <summary>Reads the bottom coordinate of <paramref name="element"/>; <see cref="int.MinValue"/> means unset.</summary>
    public static int GetBottom(System.Windows.Forms.Control element)
        => (int)(BottomProperty.GetValue(element) ?? Unset);

    /// <summary>Writes the bottom coordinate of <paramref name="element"/>.</summary>
    public static void SetBottom(System.Windows.Forms.Control element, int value)
        => BottomProperty.SetValue(element, value);

    protected internal override Size MeasureCore(Size availableSize)
    {
        var width = 0;
        var height = 0;
        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                continue;
            }

            width = Math.Max(width, ChildExtent(child, horizontal: true));
            height = Math.Max(height, ChildExtent(child, horizontal: false));
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        foreach (System.Windows.Forms.Control item in Controls)
        {
            if (!XamlLayout.OccupiesSpace(item))
            {
                XamlLayout.Hide(item);
                continue;
            }

            if (item is Control xaml)
            {
                item.Visible = xaml.Visibility == Visibility.Visible;
            }

            var left = GetLeft(item);
            var top = GetTop(item);
            var right = GetRight(item);
            var bottom = GetBottom(item);
            var hasHorizontal = left != Unset || right != Unset;
            var hasVertical = top != Unset || bottom != Unset;
            if (!hasHorizontal && !hasVertical)
            {
                continue;
            }

            var desired = XamlLayout.Measure(item, ClientSize);
            var itemWidth = item.Width > 0 ? item.Width : desired.Width;
            var itemHeight = item.Height > 0 ? item.Height : desired.Height;
            var x = left != Unset ? left : right != Unset ? ClientSize.Width - right - itemWidth : item.Left;
            var y = top != Unset ? top : bottom != Unset ? ClientSize.Height - bottom - itemHeight : item.Top;
            var width = left != Unset && right != Unset
                ? Math.Max(0, ClientSize.Width - left - right)
                : itemWidth;
            var height = top != Unset && bottom != Unset
                ? Math.Max(0, ClientSize.Height - top - bottom)
                : itemHeight;

            var bounds = new Rectangle(x, y, width, height);
            if (item.Bounds != bounds)
            {
                item.Bounds = bounds;
            }
        }
    }

    private static int ChildExtent(System.Windows.Forms.Control child, bool horizontal)
    {
        var start = horizontal ? GetLeft(child) : GetTop(child);
        var end = horizontal ? GetRight(child) : GetBottom(child);
        var desired = XamlLayout.Measure(child, Size.Empty);
        var extent = horizontal
            ? child.Width > 0 ? child.Width : desired.Width
            : child.Height > 0 ? child.Height : desired.Height;
        if (start != Unset && end != Unset)
        {
            return start + end + extent;
        }

        var position = start != Unset ? start : 0;
        return position + extent;
    }

    private static void OnPositionPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Parent?.PerformLayout();
        }
    }
}

/// <summary>
/// The base class for XAML views with a code-behind file.
/// </summary>
/// <remarks>
/// The type exists so views can write <c>public partial class MyView : UserControl</c> without importing the XAML
/// namespace through an alias. It behaves as a normal <see cref="Control"/>.
/// </remarks>
public class UserControl : Control
{
    /// <summary>Creates the view.</summary>
    public UserControl()
    {
    }
}
