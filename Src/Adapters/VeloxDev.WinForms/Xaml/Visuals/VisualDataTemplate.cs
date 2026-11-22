namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// A factory that creates one visual element for a data item in the HWND-free visual backend.
/// </summary>
/// <remarks>
/// The native WinForms <c>DataTemplate</c> and <c>TemplateSelector</c> types remain the ones the HWND-backed XAML
/// runtime uses; this type is the visual backend's counterpart and is deliberately named apart from them.
/// </remarks>
public sealed class VisualDataTemplate
{
    /// <summary>Creates a template from a factory.</summary>
    /// <param name="factory">The factory invoked for each instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public VisualDataTemplate(Func<VisualElement> factory)
        => Factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>The factory that creates an instance.</summary>
    public Func<VisualElement> Factory { get; }

    /// <summary>Creates a new element from this template.</summary>
    public VisualElement Create() => Factory();
}
