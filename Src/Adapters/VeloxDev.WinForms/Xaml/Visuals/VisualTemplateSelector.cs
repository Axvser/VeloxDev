namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// Chooses the visual element a data item is shown with in the HWND-free visual backend.
/// </summary>
/// <remarks>
/// This is the item → view seam used by the workflow surface's view pool and by callers that pick a whole
/// <see cref="VisualElement"/> for an item. For item content inside an <c>ItemsControl</c>, use
/// <see cref="DataTemplateSelector"/> instead.
/// </remarks>
public abstract class VisualTemplateSelector
{
    /// <summary>Returns the element to create for <paramref name="item"/>, or <see langword="null"/> for the default.</summary>
    /// <param name="item">The data item being shown.</param>
    public abstract VisualElement? SelectTemplate(object? item);
}
