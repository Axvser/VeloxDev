using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Enum router node (routes the merge report by the computed grade): header badge shows the
/// currently selected enum member; the right edge carries one port per enum member with aligned labels.</summary>
internal sealed class EnumSelectorNodeView : NodeViewBase
{
    protected override Brush Accent => NodeChrome.AccentBlue;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => (node as EnumSelectorNodeViewModel)?.SelectedValue?.ToString() ?? "-";

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (EnumSelectorNodeViewModel)node;
        var body = new StackPanel { Margin = new Thickness(12, 6, 0, 0), Spacing = 4 };
        body.Children.Add(new TextBlock
        {
            Text = $"Type: {vm.EnumType?.Name ?? "-"}",
            Foreground = NodeChrome.SubFg,
            FontSize = 11,
        });
        content.Children.Add(body);
        AddOutputLabels(content);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (propertyName is nameof(EnumSelectorNodeViewModel.SelectedValue) && StatusText is not null && Node is EnumSelectorNodeViewModel vm)
        {
            StatusText.Text = vm.SelectedValue?.ToString() ?? "-";
        }
    }
}
