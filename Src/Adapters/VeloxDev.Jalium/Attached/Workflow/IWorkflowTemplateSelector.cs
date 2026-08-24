using Jalium.UI;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Resolves a host control (view) for a workflow item, mirroring the role of a
/// DataTemplateSelector in the XAML adapters.</summary>
public interface IWorkflowTemplateSelector
{
    /// <summary>Creates a new view control for the specified workflow item.</summary>
    FrameworkElement CreateView(object item);
}
