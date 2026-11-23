namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>
/// A visual control with one content child, matching the platform user-control shape.
/// </summary>
/// <remarks>
/// It is the basic-element root a generated visual view declares. XAML children are assigned through
/// <see cref="Add{T}"/>, which writes <see cref="ContentControl.Content"/>; the content is then measured and
/// arranged like any other visual element.
/// </remarks>
public class UserControl : ContentControl
{
    /// <summary>Assigns the single content child and returns it.</summary>
    /// <param name="child">The visual element to show as the control's content.</param>
    /// <typeparam name="T">The child's concrete type.</typeparam>
    /// <returns>The added child.</returns>
    public new T Add<T>(T child) where T : VisualElement
    {
        Content = child;
        return child;
    }
}
