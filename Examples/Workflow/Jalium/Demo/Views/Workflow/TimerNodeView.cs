using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// 定时器数据源节点：标题行（标题 + 执行序号）+ 主体里的间隔与最近一次 tick。
/// 骨架照这套设计（Avalonia TimerNodeView.axaml）：32 / *，标签是小写号，最近一次 tick 用类型色加重。
/// 单口节点，输入口与输出口都在卡边中点上，由表面画。
/// </summary>
internal sealed class TimerNodeView : NodeViewBase
{
    private TextBox? _interval;
    private TextBlock? _lastTick;

    protected override Color Accent => CardPalette.AccentTimerPython;

    protected override string InitialExecOrder(IWorkflowNodeViewModel node)
        => node is TimerNodeViewModel { HasExecutionOrder: true } vm ? vm.ExecutionOrderText : string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (TimerNodeViewModel)node;
        var body = new StackPanel { Margin = new Thickness(12, 9), Spacing = 5 };

        body.Children.Add(NodeChrome.Label("INTERVAL (MS)"));
        _interval = NodeChrome.Field();
        _interval.Text = vm.IntervalMilliseconds.ToString();
        _interval.TextChanged += (_, _) =>
        {
            // 手输到一半（空串、"-"）不往回写，免得把视图模型里的值清成 0；解析成功才同步
            if (int.TryParse(_interval.Text, out int ms))
            {
                vm.IntervalMilliseconds = ms;
            }
        };
        body.Children.Add(_interval);

        var lastLabel = NodeChrome.Label("LAST TICK");
        lastLabel.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(lastLabel);

        _lastTick = NodeChrome.Value(vm.LastTick);
        _lastTick.Foreground = new SolidColorBrush(CardPalette.AccentTimerPython);
        _lastTick.FontWeight = FontWeights.SemiBold;
        _lastTick.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(_lastTick);

        content.Children.Add(body);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (Node is not TimerNodeViewModel vm)
        {
            return;
        }

        if (propertyName is nameof(TimerNodeViewModel.LastTick) && _lastTick is not null)
        {
            _lastTick.Text = vm.LastTick;
        }

        if (propertyName is nameof(TimerNodeViewModel.ExecutionOrderText))
        {
            SetExecOrder(vm.HasExecutionOrder ? vm.ExecutionOrderText : string.Empty);
        }
    }
}
