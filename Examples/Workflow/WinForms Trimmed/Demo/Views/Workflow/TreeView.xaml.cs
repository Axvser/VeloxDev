// VeloxDev customization: the tree is a visual view; MainForm hosts it and attaches WorkflowSurfaceBehavior.

namespace Demo.Views.Workflow;

public sealed partial class TreeView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    public TreeView()
    {
        InitializeComponent();
    }
}
