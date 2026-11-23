// VeloxDev customization: the card is composed in XAML; the workflow surface binds it to the node model.

namespace Demo.Views.Workflow;

/// <summary>A workflow node card composed entirely in XAML.</summary>
public sealed partial class NodeView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    public NodeView()
    {
        InitializeComponent();
    }
}
