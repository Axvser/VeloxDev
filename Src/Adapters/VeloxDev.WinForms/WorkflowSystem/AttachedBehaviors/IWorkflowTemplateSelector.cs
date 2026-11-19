using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Resolves the control class for one workflow item, the WinForms substitute for a <c>DataTemplateSelector</c>.
/// </summary>
/// <remarks>
/// WinForms has no XAML, no <c>DataTemplate</c> and no binding engine, so the pool's only creation path is this
/// interface. Implement it — or derive <see cref="WorkflowTemplateSelector"/> — and hand the instance to
/// <see cref="ViewPool.SetTemplateSelector"/>.
/// </remarks>
public interface IWorkflowTemplateSelector
{
    /// <summary>Creates a new view control for <paramref name="item"/>.</summary>
    /// <param name="item">The workflow item to render.</param>
    /// <returns>The control for that item.</returns>
    Control CreateView(object item);
}
