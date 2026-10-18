using System.Windows.Controls;

namespace Demo.Views.Workflow;

public partial class PythonNodeView : UserControl
{
    public PythonNodeView()
    {
        InitializeComponent();
        AgentSelectionVisual.Attach(this);
    }
}
