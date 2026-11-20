using System;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Turns a workflow item into a WinForms control through the host's <see cref="TemplateSelector"/>.</summary>
/// <remarks>
/// The selector returns a <see cref="DataTemplate"/> and the factory creates its content. WinForms has no template
/// cache to search, so the selector stays the single creation path for every item kind.
/// </remarks>
public sealed class ViewFactory : IViewFactory<IWorkflowViewModel, Control>
{
    private readonly TemplateSelector selector;

    /// <summary>Creates a factory that delegates view creation to <paramref name="selector"/>.</summary>
    /// <param name="selector">The host's item-to-control selector.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    public ViewFactory(TemplateSelector selector)
    {
        this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    /// <inheritdoc />
    public bool WantsItem(IWorkflowViewModel item) => true;

    /// <inheritdoc />
    public Control CreateView(IWorkflowViewModel item)
    {
        var template = selector.SelectTemplate(item)
            ?? throw new InvalidOperationException($"No DataTemplate applies to '{item?.GetType().FullName}'.");
        return template.CreateContent();
    }
}
