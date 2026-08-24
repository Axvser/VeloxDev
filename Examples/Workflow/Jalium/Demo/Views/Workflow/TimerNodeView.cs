using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Timer data-source node: title + last tick in the header, an interval readout in the body,
/// and a single input/output port pair.</summary>
internal sealed class TimerNodeView : NodeViewBase
{
    protected override Brush Accent => NodeChrome.AccentGreen;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => (node as TimerNodeViewModel)?.LastTick ?? string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (TimerNodeViewModel)node;
        var body = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = $"Interval: {vm.IntervalMilliseconds} ms",
            Foreground = NodeChrome.SubFg,
            FontSize = 11,
        });
        content.Children.Add(body);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (propertyName is nameof(TimerNodeViewModel.LastTick) && StatusText is not null && Node is TimerNodeViewModel vm)
        {
            StatusText.Text = vm.LastTick;
        }
    }
}
