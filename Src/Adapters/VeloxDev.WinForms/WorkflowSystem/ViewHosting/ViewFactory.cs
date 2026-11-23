using Easel.Visuals;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Resolves a visual view for one workflow item through the adapter's <see cref="VisualTemplateSelector"/>.</summary>
/// <remarks>
/// The visual selector returns a ready element rather than a template, so there is nothing to cache per item type:
/// every call creates the element its template describes. A <see langword="null"/> selector refuses every item, and
/// the pool then materializes nothing until the selector is set and the system rebuilt.
/// </remarks>
public sealed class ViewFactory : IViewFactory<IWorkflowComponent, VisualElement>
{
    private readonly VisualTemplateSelector? _selector;

    /// <summary>Creates a factory that resolves views from <paramref name="selector"/>.</summary>
    /// <param name="selector">The selector that creates one element per item, or <see langword="null"/>.</param>
    public ViewFactory(VisualTemplateSelector? selector)
    {
        _selector = selector;
    }

    /// <inheritdoc />
    public bool WantsItem(IWorkflowComponent item) => true;

    /// <inheritdoc />
    public VisualElement? CreateView(IWorkflowComponent item) => _selector?.SelectTemplate(item);
}
