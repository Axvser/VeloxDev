using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Logic-gate decision node: header badge shows the gate operation + last routing result,
/// right edge carries the True (top) / False (bottom) ports with aligned labels.</summary>
internal sealed class LogicGateNodeView : NodeViewBase
{
    protected override Brush Accent => NodeChrome.AccentViolet;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => node is LogicGateNodeViewModel lg ? $"{lg.GateOp} · {lg.LastRouted}" : string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (LogicGateNodeViewModel)node;
        var body = new StackPanel { Margin = new Thickness(12, 6, 0, 0), Spacing = 4 };
        body.Children.Add(new TextBlock
        {
            Text = $"Gate: {vm.GateOp}",
            Foreground = NodeChrome.SubFg,
            FontSize = 11,
        });
        content.Children.Add(body);
        AddOutputLabels(content);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (propertyName is nameof(LogicGateNodeViewModel.LastRouted) && StatusText is not null && Node is LogicGateNodeViewModel vm)
        {
            StatusText.Text = $"{vm.GateOp} · {vm.LastRouted}";
        }
    }
}
