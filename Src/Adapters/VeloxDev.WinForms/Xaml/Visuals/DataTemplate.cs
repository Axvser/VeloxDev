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
