using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Generic task-executor node card: title + status in the header, a delay readout in the
/// body, one input port on the left and one output port on the right (drawn by the base at the
/// NodePorts centers the surface hit-tests).</summary>
internal sealed class NodeView : NodeViewBase
{
    protected override Brush Accent => NodeChrome.DefaultBorder;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => (node as NodeViewModel)?.LastStatus ?? string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (NodeViewModel)node;
        var body = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = $"Delay: {vm.DelayMilliseconds} ms",
            Foreground = NodeChrome.SubFg,
            FontSize = 11,
        });
        content.Children.Add(body);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (propertyName is nameof(NodeViewModel.LastStatus) && StatusText is not null && Node is NodeViewModel vm)
        {
            StatusText.Text = vm.LastStatus;
        }
    }
}
