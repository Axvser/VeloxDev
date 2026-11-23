using VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>Creates one visual element for each item realized by a visual <c>ItemsControl</c>.</summary>
/// <remarks>
/// It carries a factory rather than markup because the source generator compiles the template body into a method and passes that
/// method here.
/// </remarks>
public sealed class DataTemplate
{
    /// <summary>Creates a template from a factory.</summary>
    /// <param name="factory">The factory invoked for each instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public DataTemplate(Func<VisualElement> factory)
        => Factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>The factory that creates an instance.</summary>
    public Func<VisualElement> Factory { get; }

    /// <summary>Creates a new element from this template.</summary>
    public VisualElement Create() => Factory();
}

/// <summary>Chooses the <see cref="DataTemplate"/> used to present a piece of content.</summary>
/// <remarks>
/// A selector is referenced from XAML resources and assigned to a content template selector property. Its non-null
/// result wins over a static <see cref="DataTemplate"/> at the same level, so one control can present different
/// templates for different content values.
/// </remarks>
public abstract class DataTemplateSelector
{
    /// <summary>Returns the template that presents <paramref name="item"/>, or <see langword="null"/>.</summary>
    /// <param name="item">The raw content being presented.</param>
    /// <param name="container">The <see cref="ContentControl"/> that owns the content, or <see langword="null"/> when it is unknown.</param>
    /// <returns>The template to apply, or <see langword="null"/> to continue with the next template source.</returns>
    public abstract DataTemplate? SelectTemplate(object? item, ContentControl? container);
}
