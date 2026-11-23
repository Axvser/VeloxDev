using System.Drawing;
using System.Runtime.CompilerServices;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>Places children at absolute coordinates without affecting the panel's own desired size.</summary>
/// <remarks>
/// <para>
/// Each child is positioned through the attached <see cref="GetLeft"/>, <see cref="GetTop"/>,
/// <see cref="GetRight"/> and <see cref="GetBottom"/> values. Left wins over right and top wins over bottom; an
/// unset value on both sides places the child at the content origin.
/// </para>
/// <para>
/// A canvas measures its children without a size constraint and reports only its padding, so give it an explicit
/// <see cref="VisualElement.Width"/> and <see cref="VisualElement.Height"/> when its own size should not come from
/// the parent.
/// </para>
/// </remarks>
public class Canvas : Panel
{
    /// <summary>The attached left offset; <see cref="double.NaN"/> leaves it unset.</summary>
    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.RegisterAttached(
            "Left", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN) { AffectsRender = false });

    /// <summary>The attached top offset; <see cref="double.NaN"/> leaves it unset.</summary>
    public static readonly DependencyProperty TopProperty =
        DependencyProperty.RegisterAttached(
            "Top", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN) { AffectsRender = false });

    /// <summary>The attached right offset; <see cref="double.NaN"/> leaves it unset.</summary>
    public static readonly DependencyProperty RightProperty =
        DependencyProperty.RegisterAttached(
            "Right", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN) { AffectsRender = false });

    /// <summary>The attached bottom offset; <see cref="double.NaN"/> leaves it unset.</summary>
    public static readonly DependencyProperty BottomProperty =
        DependencyProperty.RegisterAttached(
            "Bottom", typeof(double), typeof(Canvas),
            new PropertyMetadata(double.NaN) { AffectsRender = false });

    /// <summary>Reads the left offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The offset in pixels; <see cref="double.NaN"/> when unset.</returns>
    public static double GetLeft(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return (double)element.GetValue(LeftProperty)!;
    }

    /// <summary>Writes the left offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The offset in pixels, or <see cref="double.NaN"/> to unset it.</param>
    public static void SetLeft(VisualElement element, double value) => element.SetValue(LeftProperty, value);

    /// <summary>Reads the top offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The offset in pixels; <see cref="double.NaN"/> when unset.</returns>
    public static double GetTop(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return (double)element.GetValue(TopProperty)!;
    }

    /// <summary>Writes the top offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The offset in pixels, or <see cref="double.NaN"/> to unset it.</param>
    public static void SetTop(VisualElement element, double value) => element.SetValue(TopProperty, value);

    /// <summary>Reads the right offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The offset in pixels; <see cref="double.NaN"/> when unset.</returns>
    public static double GetRight(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return (double)element.GetValue(RightProperty)!;
    }

    /// <summary>Writes the right offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The offset in pixels, or <see cref="double.NaN"/> to unset it.</param>
    public static void SetRight(VisualElement element, double value) => element.SetValue(RightProperty, value);

    /// <summary>Reads the bottom offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The offset in pixels; <see cref="double.NaN"/> when unset.</returns>
    public static double GetBottom(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return (double)element.GetValue(BottomProperty)!;
    }

    /// <summary>Writes the bottom offset of <paramref name="element"/>.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The offset in pixels, or <see cref="double.NaN"/> to unset it.</param>
    public static void SetBottom(VisualElement element, double value) => element.SetValue(BottomProperty, value);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in ArrangedChildren())
        {
            child.Measure(Size.Empty);
        }

        var padding = Padding;
        return new Size((int)Math.Ceiling(padding.Horizontal), (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        var width = Math.Max(0f, finalSize.Width - padding.Horizontal);
        var height = Math.Max(0f, finalSize.Height - padding.Vertical);

        foreach (var child in ArrangedChildren())
        {
            var placement = PlacementOf(child);
            var desired = child.DesiredSize;
            var x = !double.IsNaN(placement.Left)
                ? placement.Left
                : !double.IsNaN(placement.Right)
                    ? width - placement.Right - desired.Width
                    : 0d;
            var y = !double.IsNaN(placement.Top)
                ? placement.Top
                : !double.IsNaN(placement.Bottom)
                    ? height - placement.Bottom - desired.Height
                    : 0d;

            child.Arrange(new RectangleF(
                padding.Left + (float)x,
                padding.Top + (float)y,
                desired.Width,
                desired.Height));
        }
    }

    private static (double Left, double Top, double Right, double Bottom) PlacementOf(VisualElement element)
        => (GetLeft(element), GetTop(element), GetRight(element), GetBottom(element));

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
