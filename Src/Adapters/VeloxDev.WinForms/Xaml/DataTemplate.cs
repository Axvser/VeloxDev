namespace VeloxDev.WinForms.Xaml;

/// <summary>A resource that creates a fresh <see cref="System.Windows.Forms.Control"/> tree when it is applied to an item.</summary>
/// <remarks>
/// The generator emits the factory from the XAML body. This type intentionally does not know about data binding:
/// the consumer that instantiates the template is responsible for assigning each created control's data context.
/// </remarks>
public sealed class DataTemplate
{
    private readonly Func<System.Windows.Forms.Control> _factory;

    /// <summary>Creates a template over <paramref name="factory"/>.</summary>
    /// <param name="factory">Creates one instance of the template's visual tree.</param>
    public DataTemplate(Func<System.Windows.Forms.Control> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Creates one instance of the template's visual tree.</summary>
    public System.Windows.Forms.Control CreateContent() => _factory();
}
