namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// Chooses the visual element a data item is shown with in the HWND-free visual backend.
/// </summary>
/// <remarks>
/// The native WinForms <c>TemplateSelector</c> is unchanged and still serves the HWND-backed XAML runtime. A host
/// using <see cref="WorkflowSystem.VisualHosting.VisualWorkflowSurface"/> can consult this selector in its node
/// factory when different node types need different cards.
/// </remarks>
public abstract class VisualTemplateSelector
{
    /// <summary>Returns the element to create for <paramref name="item"/>, or <see langword="null"/> for the default.</summary>
    /// <param name="item">The data item being shown.</param>
    public abstract VisualElement? SelectTemplate(object? item);
}
