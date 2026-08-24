using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Bool selector node: routes the input to the True/False output slots based on Condition.
/// Header badge shows the routing condition; right edge carries the True (top) / False (bottom) ports
/// with aligned labels.</summary>
internal sealed class BoolSelectorNodeView : NodeViewBase
{
    protected override Brush Accent => NodeChrome.AccentYellow;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => node is BoolSelectorNodeViewModel { Condition: true } ? "True" : "False";

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (BoolSelectorNodeViewModel)node;
        var condition = new CheckBox
        {
            Content = "Condition",
            IsChecked = vm.Condition,
            Foreground = NodeChrome.SubFg,
            Margin = new Thickness(12, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        condition.Checked += (_, _) => { if (vm is not null) vm.Condition = true; };
        condition.Unchecked += (_, _) => { if (vm is not null) vm.Condition = false; };
        content.Children.Add(condition);
        AddOutputLabels(content);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (propertyName is nameof(BoolSelectorNodeViewModel.Condition) && StatusText is not null && Node is BoolSelectorNodeViewModel vm)
        {
            StatusText.Text = vm.Condition ? "True" : "False";
        }
    }
}
