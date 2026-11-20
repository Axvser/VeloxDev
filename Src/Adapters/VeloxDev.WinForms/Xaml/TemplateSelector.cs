namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Chooses a <see cref="DataTemplate"/> for one item, the WinForms counterpart of a platform template selector.
/// </summary>
/// <remarks>
/// This type is deliberately workflow-agnostic: it knows only <see cref="object"/> and <see cref="DataTemplate"/>.
/// A host derives from it, exposes the templates it supports, and returns the one that applies to the item.
/// </remarks>
public abstract class TemplateSelector
{
    /// <summary>Returns the template for <paramref name="item"/>, or <see langword="null"/> when none applies.</summary>
    /// <param name="item">The item the pool is about to materialize.</param>
    public abstract DataTemplate? SelectTemplate(object? item);
}
