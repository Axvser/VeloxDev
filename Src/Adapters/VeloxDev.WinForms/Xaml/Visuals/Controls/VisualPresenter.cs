using System.Drawing;
using VeloxDev.WinForms.Xaml;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>Hosts a lightweight <see cref="Visual"/> tree (or another <see cref="VisualElement"/>) inside the control tree.</summary>
/// <remarks>
/// A <see cref="ContainerVisual"/> is not a <see cref="VisualElement"/>, so it cannot be added as a child of a
/// visual element. This presenter gives it a rectangular slot; a <see cref="VisualElement"/> content is measured and
/// arranged like any other child, while a plain visual keeps its own <see cref="Visual.Bounds"/> children and is
/// translated to the presenter's origin.
/// </remarks>
public class VisualPresenter : VisualElement
{
    /// <summary>The hosted visual tree.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(Visual), typeof(VisualPresenter),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Creates a non-hit-testable presentation slot.</summary>
    public VisualPresenter()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The hosted visual tree.</summary>
    public Visual? Content
    {
        get => (Visual?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Content is VisualElement element)
        {
            return element.Measure(availableSize);
        }

        if (Content is { } visual && visual.Bounds.Width > 0f && visual.Bounds.Height > 0f)
        {
            return new Size(
                (int)Math.Ceiling(visual.Bounds.Width),
                (int)Math.Ceiling(visual.Bounds.Height));
        }

        return new Size(
            availableSize.Width > 0 ? availableSize.Width : 120,
            availableSize.Height > 0 ? availableSize.Height : 80);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (Content is VisualElement element)
        {
            element.Arrange(new RectangleF(0f, 0f, finalSize.Width, finalSize.Height));
        }
        else if (Content is { } visual)
        {
            visual.Bounds = new RectangleF(0f, 0f, finalSize.Width, finalSize.Height);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!ReferenceEquals(e.Property, ContentProperty))
        {
            return;
        }

        if (e.OldValue is Visual old && Children.Contains(old))
        {
            Remove(old);
        }

        if (e.NewValue is Visual next)
        {
            if (next.Parent is ContainerVisual parent)
            {
                parent.Remove(next);
            }

            ((ContainerVisual)this).Add(next);
        }

        InvalidateMeasure();
    }
}
