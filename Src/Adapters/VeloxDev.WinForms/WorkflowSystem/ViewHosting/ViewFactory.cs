using System;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Turns a workflow item into a WinForms control through the host's <see cref="IWorkflowTemplateSelector"/>.</summary>
/// <remarks>
/// This is the WinForms substitute for a <c>DataTemplateSelector</c>: the selector decides which control class an
/// item gets, and the factory forwards every item to it. WinForms has no template cache to search, so the selector
/// stays the single creation path for nodes, slots, links and trees alike.
/// </remarks>
public sealed class ViewFactory : IViewFactory<IWorkflowViewModel, Control>
{
    private readonly IWorkflowTemplateSelector selector;

    /// <summary>Creates a factory that delegates view creation to <paramref name="selector"/>.</summary>
    /// <param name="selector">The host's item-to-control selector.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    public ViewFactory(IWorkflowTemplateSelector selector)
    {
        this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    /// <inheritdoc />
    public bool WantsItem(IWorkflowViewModel item) => true;

    /// <inheritdoc />
    public Control? CreateView(IWorkflowViewModel item) => selector.CreateView(item);
}
