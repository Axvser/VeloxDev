// VeloxDev customization: the card is composed in NodeView.xaml; this file only runs the generated initializer.
using VeloxDev.WinForms.Xaml.Controls;

namespace Demo.Views.Workflow;

/// <summary>A workflow node card composed entirely in XAML.</summary>
public sealed partial class NodeView : UserControl
{
    /// <summary>Creates the card and runs the generated initializer.</summary>
    public NodeView()
    {
        InitializeComponent();
    }
}
