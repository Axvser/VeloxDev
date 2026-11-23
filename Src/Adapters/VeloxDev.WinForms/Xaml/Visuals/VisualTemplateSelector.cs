namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// Chooses the visual element a data item is shown with in the HWND-free visual backend.
/// </summary>
/// <remarks>
/// The native WinForms <c>TemplateSelector</c> is unchanged and still serves the HWND-backed XAML runtime. This is
/// its visual-backend counterpart: a host that renders a visual tree consults it before any fallback.
/// </remarks>
public abstract class VisualTemplateSelector
{
    /// <summary>Returns the element to create for <paramref name="item"/>, or <see langword="null"/> for the default.</summary>
    /// <param name="item">The data item being shown.</param>
    public abstract VisualElement? SelectTemplate(object? item);
}
