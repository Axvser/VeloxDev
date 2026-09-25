using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// 工作流发起节点：可编辑的种子负载 + 一排 Compile / Run / Stop / Close。
/// 骨架照这套设计（Avalonia ControllerView.axaml）：标题行 32 高，主体可编辑，动作区 66 高、
/// 四颗同形的幽灵按钮按 2×2 摆，语义只在文字颜色上（Run 要编译过才亮）。拖拽与连线归表面，端口归表面。
/// </summary>
internal sealed class ControllerView : NodeViewBase
{
    private TextBox? _seed;
    private Button? _run;

    protected override Color Accent => CardPalette.AccentController;

    // 控制器视图模型不发布 Title，卡片自己起名 —— 与这套设计的 Avalonia / WinUI / MAUI 三张卡同字
    protected override string TitleFor(IWorkflowNodeViewModel node) => "Controller";

    // 这张卡没有状态胶囊：设计里头部只有类型色条 + 标题（原来是右上角一颗 Running/Idle 的灰字）
    protected override string InitialStatus(IWorkflowNodeViewModel node) => string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (ControllerViewModel)node;

        // 主体 * / 66：上面是可编辑的种子，下面那条是动作区（与设计稿同一副骨架）
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(66) });

        var seedPanel = new StackPanel { Margin = new Thickness(12, 9), Spacing = 5 };
        seedPanel.Children.Add(NodeChrome.Label("SEED"));

        _seed = NodeChrome.Field();
        _seed.Text = vm.SeedPayload;
        _seed.TextChanged += (_, _) => vm.SeedPayload = _seed.Text ?? string.Empty;
        seedPanel.Children.Add(_seed);

        Grid.SetRow(seedPanel, 0);
        content.Children.Add(seedPanel);

        // 动作区上沿那条分隔线：与标题行下沿那条同一种线，只是贴在另一格的上沿
        var divider = NodeChrome.Divider(atBottom: false);
        Grid.SetRow(divider, 1);
        content.Children.Add(divider);

        var actions = new Grid { Margin = new Thickness(12, 7), ColumnSpacing = 6, RowSpacing = 6 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

        var compile = NodeChrome.Ghost("Compile", CardPalette.GhostCompileText);
        compile.Click += (_, _) => vm.CompileCommand.Execute(null);

        _run = NodeChrome.Ghost("Run", CardPalette.GhostRunText);
        _run.Click += (_, _) => vm.RunCommand.Execute(null);
        NodeChrome.SetGhostEnabled(_run, CardPalette.GhostRunText, vm.HasCompiledGraphs);

        var stop = NodeChrome.Ghost("Stop", CardPalette.GhostStopText);
        stop.Click += (_, _) => vm.StopCommand.Execute(null);

        var close = NodeChrome.Ghost("Close", CardPalette.GhostCloseText);
        close.Click += (_, _) => vm.CloseWorkflowCommand.Execute(null);

        Place(actions, compile, 0, 0);
        Place(actions, _run, 0, 1);
        Place(actions, stop, 1, 0);
        Place(actions, close, 1, 1);

        Grid.SetRow(actions, 1);
        content.Children.Add(actions);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (Node is not ControllerViewModel vm)
        {
            return;
        }

        // Run 只在编译出图之后才亮；禁用态只压暗边与字，形状不变（见 NodeChrome.SetGhostEnabled）
        if (propertyName is nameof(ControllerViewModel.HasCompiledGraphs) && _run is not null)
        {
            NodeChrome.SetGhostEnabled(_run, CardPalette.GhostRunText, vm.HasCompiledGraphs);
        }
    }

    private static void Place(Grid grid, FrameworkElement child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
