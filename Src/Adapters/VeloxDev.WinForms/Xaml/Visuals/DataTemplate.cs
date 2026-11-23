using VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>Creates one visual element for each item realized by a visual <c>ItemsControl</c>.</summary>
/// <remarks>
/// <para>
/// It carries a factory rather than markup because the source generator compiles the template body into a method and passes that
/// method here.
/// </para>
/// <para>
/// Each <see cref="Create"/> call gets its own <see cref="VisualNameScope"/>: an <c>x:Name</c> inside the template is a
/// part of that one instance, and a binding or <c>ElementName</c> inside it never resolves to another item's part. The
/// scope is attached to the created root, which is where ancestor lookups stop.
/// </para>
/// </remarks>
public sealed class DataTemplate
{
    private readonly Func<VisualNameScope, VisualElement> _factory;

    /// <summary>Creates a template from a scope-aware factory.</summary>
    /// <param name="factory">The factory invoked for each instance; the argument is that instance's name scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public DataTemplate(Func<VisualNameScope, VisualElement> factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>Creates a template from a factory that does not use a name scope.</summary>
    /// <param name="factory">The factory invoked for each instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public DataTemplate(Func<VisualElement> factory)
        : this(_ => (factory ?? throw new ArgumentNullException(nameof(factory)))())
    {
    }

    /// <summary>The factory that creates an instance, kept for callers that read the delegate.</summary>
    /// <remarks>Each invocation creates a fresh <see cref="VisualNameScope"/> through <see cref="Create"/>.</remarks>
    public Func<VisualElement> Factory => Create;

    /// <summary>Creates a new element from this template.</summary>
    public VisualElement Create()
    {
        var scope = new VisualNameScope();
        var root = _factory(scope);
        root.SetNameScope(scope);
        return root;
    }
}

/// <summary>Chooses the <see cref="DataTemplate"/> used to present a piece of content.</summary>
/// <remarks>
/// <para>
/// A selector is referenced from XAML resources and assigned to a content template selector property. Its non-null
/// result wins over a static <see cref="DataTemplate"/> at the same level, so one control can present different
/// templates for different content values.
/// </para>
/// <para>
/// Unlike <see cref="VisualTemplateSelector"/>, which returns one element for the item-to-view seam, this selector
/// returns a factory together with the owning container. <see cref="DataTemplateSelectorAdapter"/> bridges a selector
/// of this kind into that seam.
/// </para>
/// </remarks>
public abstract class DataTemplateSelector
{
    /// <summary>Returns the template that presents <paramref name="item"/>, or <see langword="null"/>.</summary>
    /// <param name="item">The raw content being presented.</param>
    /// <param name="container">The <see cref="ContentControl"/> that owns the content, or <see langword="null"/> when it is unknown.</param>
    /// <returns>The template to apply, or <see langword="null"/> to continue with the next template source.</returns>
    public abstract DataTemplate? SelectTemplate(object? item, ContentControl? container);
}
