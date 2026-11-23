using System.Drawing;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>
/// A template that creates the panel which hosts the realized item visuals of an <see cref="ItemsControl"/>.
/// </summary>
/// <remarks>
/// It mirrors <see cref="DataTemplate"/>: the source generator compiles the panel markup into a factory and passes
/// that method here. Each <see cref="Create"/> call gets a fresh <see cref="VisualNameScope"/>, so a named part inside
/// the panel belongs to that one item host.
/// </remarks>
public sealed class ItemsPanelTemplate
{
    private readonly Func<VisualNameScope, Panel> _factory;

    /// <summary>Creates a template from a scope-aware factory.</summary>
    /// <param name="factory">The factory invoked for each item host; the argument is that instance's name scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public ItemsPanelTemplate(Func<VisualNameScope, Panel> factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>Creates a template from a factory that does not use a name scope.</summary>
    /// <param name="factory">The factory invoked for each item host.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public ItemsPanelTemplate(Func<Panel> factory)
        : this(_ => (factory ?? throw new ArgumentNullException(nameof(factory)))())
    {
    }

    /// <summary>Creates one panel instance.</summary>
    public Panel Create()
    {
        var scope = new VisualNameScope();
        var panel = _factory(scope);
        panel.SetNameScope(scope);
        return panel;
    }
}

/// <summary>
/// The default items host: it stacks realized item visuals vertically.
/// </summary>
/// <remarks>
/// An <see cref="ItemsControl"/> uses a presenter declared in its content before falling back to stacking the items
/// itself. <see cref="ItemsControl.GetIsItemsHost"/> marks a panel the same way, so a custom panel can be declared as
/// the host without a template.
/// </remarks>
public class ItemsPresenter : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var contentWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal));
        var width = 0;
        var height = 0;
        foreach (var child in Children)
        {
            if (child is not VisualElement element || element.Visibility == XamlVisibility.Collapsed)
            {
                continue;
            }

            var measured = element.Measure(new Size(contentWidth, 0));
            width = Math.Max(width, measured.Width);
            height += measured.Height;
        }

        return new Size(
            width + (int)Math.Ceiling(padding.Horizontal),
            height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        var content = new RectangleF(
            (int)Math.Ceiling(padding.Left),
            (int)Math.Ceiling(padding.Top),
            Math.Max(0f, finalSize.Width - padding.Horizontal),
            Math.Max(0f, finalSize.Height - padding.Vertical));
        var position = content.Top;
        foreach (var child in Children)
        {
            if (child is not VisualElement element || element.Visibility == XamlVisibility.Collapsed)
            {
                continue;
            }

            var desired = element.DesiredSize;
            element.Arrange(new RectangleF(content.Left, position, content.Width, desired.Height));
            position += desired.Height;
        }
    }
}
