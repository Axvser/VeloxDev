namespace Demo.Controls;

/// <summary>
/// The fallback node card — a bare chrome surface, used only for a node type that ships no view of
/// its own. It reads nothing off its DataContext (<c>IWorkflowNodeViewModel</c> carries geometry and
/// slots and no display name) and has no interior metrics to re-flow on zoom.
/// </summary>
public partial class NodeView : ContentView
{
    public NodeView()
    {
        InitializeComponent();
    }
}
