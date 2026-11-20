// VeloxDev customization: The surface owns its visual tree in TemplateClass.xaml. Its TemplateSelector resource
// picks the generated view items; give the surface an IWorkflowTreeViewModel as its data context.
using VeloxDev.WinForms.Xaml.Controls;

namespace TemplateNamespace;

/// <summary>
/// The workflow surface: a XAML tree of scroll viewer, canvas, grid decorator and minimap, wired through
/// <see cref="VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed partial class TemplateClass : UserControl
{
    /// <summary>Creates the surface and runs the generated initializer.</summary>
    public TemplateClass()
    {
        InitializeComponent();

        // VeloxDev customization: the link menu and the key/wheel policy belong to the host; see
        // WorkflowSurfaceBehavior.SetLinkMenuBuilder and the tree helper's IInputEvents.
    }
}
