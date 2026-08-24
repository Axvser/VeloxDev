using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>The "real computation" showcase node: title + status in the header, a purpose description,
/// an editable script (bound to <see cref="PythonScriptNodeViewModel.Script"/>), and a last-run · status
/// bar. Input/output port circles are drawn by the base at the NodePorts centers.</summary>
internal sealed class PythonNodeView : NodeViewBase
{
    private TextBlock? _runLine;

    protected override Brush Accent => NodeChrome.AccentBlue;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => (node as PythonScriptNodeViewModel)?.LastStatus ?? string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (PythonScriptNodeViewModel)node;
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        if (!string.IsNullOrEmpty(vm.Description))
        {
            var desc = new TextBlock
            {
                Text = vm.Description,
                Foreground = NodeChrome.SubFg,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 28,
                Margin = new Thickness(12, 6, 12, 4),
            };
            Grid.SetRow(desc, 0);
            body.Children.Add(desc);
        }

        var scriptBox = new TextBox
        {
            Text = vm.Script,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
            Background = NodeChrome.EditorBg,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
        };
        scriptBox.TextChanged += (_, _) => { if (vm is not null) vm.Script = scriptBox.Text ?? string.Empty; };
        var editor = new Border
        {
            Margin = new Thickness(12, 6, 12, 6),
            Background = NodeChrome.EditorBg,
            BorderBrush = NodeChrome.SubBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = scriptBox,
        };
        Grid.SetRow(editor, 1);
        body.Children.Add(editor);

        _runLine = new TextBlock
        {
            Text = $"{vm.LastRun} · {vm.LastStatus}",
            Foreground = NodeChrome.SubFg,
            FontSize = 10,
            Margin = new Thickness(12, 0, 12, 6),
        };
        Grid.SetRow(_runLine, 2);
        body.Children.Add(_runLine);

        content.Children.Add(body);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (Node is not PythonScriptNodeViewModel vm)
        {
            return;
        }

        if (propertyName is nameof(PythonScriptNodeViewModel.LastRun) or nameof(PythonScriptNodeViewModel.LastStatus))
        {
            if (StatusText is not null)
            {
                StatusText.Text = vm.LastStatus;
            }

            if (_runLine is not null)
            {
                _runLine.Text = $"{vm.LastRun} · {vm.LastStatus}";
            }
        }
    }
}
