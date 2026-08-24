using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Workflow initiator node: a "Network Flow Controller" card with an editable seed payload
/// and Compile / Run / Stop / Close buttons on the card (Run enabled once graphs are compiled). The
/// surface handles dragging/connecting; the ports are drawn by the base.</summary>
internal sealed class ControllerView : NodeViewBase
{
    private Button? _runButton;

    protected override Brush Accent => NodeChrome.AccentBlue;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => node is ControllerViewModel { IsActive: true } ? "Running" : "Idle";

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (ControllerViewModel)node;
        var body = new StackPanel { Margin = new Thickness(12), Spacing = 8 };

        body.Children.Add(new TextBlock { Text = "Seed", Foreground = NodeChrome.SubFg, FontSize = 11 });
        var seedBox = new TextBox
        {
            Text = vm.SeedPayload,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Padding = new Thickness(6, 4),
        };
        seedBox.TextChanged += (_, _) => { if (vm is not null) vm.SeedPayload = seedBox.Text ?? string.Empty; };
        body.Children.Add(seedBox);

        var compileBtn = NodeChrome.MakeButton("Compile", new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F)), new SolidColorBrush(Colors.White), NodeChrome.AccentBlue);
        compileBtn.Click += (_, _) => vm.CompileCommand.Execute(null);

        _runButton = NodeChrome.MakeButton("Run", new SolidColorBrush(Color.FromRgb(0x1E, 0x5F, 0x2E)), new SolidColorBrush(Colors.White), NodeChrome.AccentGreen);
        _runButton.IsEnabled = vm.HasCompiledGraphs;
        _runButton.Click += (_, _) => vm.RunCommand.Execute(null);

        var stopBtn = NodeChrome.MakeButton("Stop", new SolidColorBrush(Color.FromRgb(0x5F, 0x1E, 0x1E)), new SolidColorBrush(Colors.White), NodeChrome.AccentYellow);
        stopBtn.Click += (_, _) => vm.StopCommand.Execute(null);

        var closeBtn = NodeChrome.MakeButton("Close", NodeChrome.HeaderBg, NodeChrome.SubFg, NodeChrome.DefaultBorder);
        closeBtn.Click += (_, _) => vm.CloseWorkflowCommand.Execute(null);

        var buttons = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        buttons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        buttons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(compileBtn, 0); Grid.SetRow(compileBtn, 0); compileBtn.Margin = new Thickness(0, 0, 3, 3);
        Grid.SetColumn(_runButton, 1); Grid.SetRow(_runButton, 0); _runButton.Margin = new Thickness(3, 0, 0, 3);
        Grid.SetColumn(stopBtn, 0); Grid.SetRow(stopBtn, 1); stopBtn.Margin = new Thickness(0, 3, 3, 0);
        Grid.SetColumn(closeBtn, 1); Grid.SetRow(closeBtn, 1); closeBtn.Margin = new Thickness(3, 3, 0, 0);
        buttons.Children.Add(compileBtn);
        buttons.Children.Add(_runButton);
        buttons.Children.Add(stopBtn);
        buttons.Children.Add(closeBtn);
        body.Children.Add(buttons);

        content.Children.Add(body);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (Node is not ControllerViewModel vm)
        {
            return;
        }

        if (propertyName is nameof(ControllerViewModel.IsActive) && StatusText is not null)
        {
            StatusText.Text = vm.IsActive ? "Running" : "Idle";
        }

        if (propertyName is nameof(ControllerViewModel.HasCompiledGraphs) && _runButton is not null)
        {
            _runButton.IsEnabled = vm.HasCompiledGraphs;
        }
    }
}
