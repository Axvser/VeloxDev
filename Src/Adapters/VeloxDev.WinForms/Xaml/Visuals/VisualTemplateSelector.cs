using VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// Chooses the visual element a data item is shown with in the HWND-free visual backend.
/// </summary>
/// <remarks>
/// <para>
/// This is the item → view seam used by the workflow surface's view pool and by callers that pick a whole
/// <see cref="VisualElement"/> for an item. For item content inside an <c>ItemsControl</c>, use
/// <see cref="DataTemplateSelector"/> instead.
/// </para>
/// <para>
/// The two contracts differ in what they hand back, not in what they decide about: <see cref="DataTemplateSelector"/>
/// returns a <see cref="DataTemplate"/> (a factory, so every realization can create a fresh instance) and receives the
/// owning <c>ContentControl</c>; this selector returns the element itself. Use
/// <see cref="DataTemplateSelectorAdapter"/> to feed a <see cref="DataTemplateSelector"/> into this seam.
/// </para>
/// </remarks>
public abstract class VisualTemplateSelector
{
    /// <summary>Returns the element to create for <paramref name="item"/>, or <see langword="null"/> for the default.</summary>
    /// <param name="item">The data item being shown.</param>
    public abstract VisualElement? SelectTemplate(object? item);
}

/// <summary>Adapts a <see cref="DataTemplateSelector"/> to the item-to-view seam.</summary>
/// <remarks>
/// <para>
/// SelectTemplate creates a fresh <see cref="VisualElement"/> from the selected <see cref="DataTemplate"/> for every
/// call, which is what a reused item needs. The parameterless constructor reports <see langword="null"/> as the
/// selector's <c>container</c>; the other overload forwards the container that is known here.
/// </para>
/// <para>
/// The reverse adapter is not provided: a <see cref="VisualTemplateSelector"/> returns one concrete element, and
/// wrapping it in a <see cref="DataTemplate"/> would share that element across realizations.
/// </para>
/// </remarks>
public sealed class DataTemplateSelectorAdapter : VisualTemplateSelector
{
    private readonly DataTemplateSelector _selector;
    private readonly ContentControl? _container;

    /// <summary>Creates an adapter over <paramref name="selector"/>.</summary>
    /// <param name="selector">The template selector to delegate to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    public DataTemplateSelectorAdapter(DataTemplateSelector selector)
        : this(selector, null)
    {
    }

    /// <summary>Creates an adapter that passes <paramref name="container"/> to the selector.</summary>
    /// <param name="selector">The template selector to delegate to.</param>
    /// <param name="container">The container to report to the selector, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    public DataTemplateSelectorAdapter(DataTemplateSelector selector, ContentControl? container)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _container = container;
    }

    /// <inheritdoc />
    public override VisualElement? SelectTemplate(object? item)
        => _selector.SelectTemplate(item, _container)?.Create();
}
