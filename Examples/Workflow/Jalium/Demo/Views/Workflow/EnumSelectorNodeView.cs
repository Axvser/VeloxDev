using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// 枚举路由节点：标题行（标题 + 执行序号 + 路由结果胶囊）、主体里两个下拉，以及每枚枚举成员一颗输出口。
/// 骨架照这套设计（Avalonia EnumSelectorNodeView.axaml）：32 / *，输入口居中在主体上，输出口按行排在
/// 表头下面。每条分支的名字由表面画，与它那颗口同一行 —— 位置由 <see cref="NodePorts"/> 一处给出。
/// </summary>
internal sealed class EnumSelectorNodeView : NodeViewBase
{
    private ComboBox? _method;
    private ComboBox? _mode;

    protected override Color Accent => CardPalette.AccentEnum;

    protected override string InitialExecOrder(IWorkflowNodeViewModel node)
        => node is EnumSelectorNodeViewModel { HasExecutionOrder: true } vm ? vm.ExecutionOrderText : string.Empty;

    // 设计里这张卡的路由结果是一颗紫色加粗的胶囊，与标题行那条色条同色
    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => (node as EnumSelectorNodeViewModel)?.LastRouted ?? string.Empty;

    protected override bool StatusBold => true;

    protected override Color StatusTextColor => CardPalette.AccentEnum;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (EnumSelectorNodeViewModel)node;

        // 表头用固定像素行排，不用 StackPanel 顺着内容量：端口行的位置要由表面事先算好（它手上没有
        // 卡片的布局树），所以这段的高度必须是个常量。各段与 NodePorts.EnumBodyTop 是同一组数
        var header = new Grid { Margin = new Thickness(14, NodePorts.BodyPaddingTop, 14, 0) };
        AddFixedRow(header, NodePorts.LabelRowHeight);   // 0 标签
        AddFixedRow(header, NodePorts.RowSpacing);       // 1
        AddFixedRow(header, NodePorts.FieldRowHeight);   // 2 下拉
        AddFixedRow(header, NodePorts.GroupSpacing);     // 3
        AddFixedRow(header, NodePorts.LabelRowHeight);   // 4 标签
        AddFixedRow(header, NodePorts.RowSpacing);       // 5
        AddFixedRow(header, NodePorts.FieldRowHeight);   // 6 下拉
        AddFixedRow(header, NodePorts.GroupSpacing);     // 7
        AddFixedRow(header, NodePorts.LabelRowHeight);   // 8 标签
        AddFixedRow(header, NodePorts.RowSpacing);       // 9

        Add(header, NodeChrome.Label("SELECTED METHOD"), 0);
        Add(header, NodeChrome.Label("COMPILE MODE"), 4);
        Add(header, NodeChrome.Label("OUTPUT SLOTS"), 8);

        _method = NodeChrome.Picker();
        _method.ItemsSource = vm.EnumValues;
        _method.SelectedItem = vm.SelectedValue;
        _method.SelectionChanged += (_, _) =>
        {
            if (_method.SelectedItem is { } value)
            {
                vm.SelectedValue = value;
            }
        };
        Add(header, _method, 2);

        _mode = NodeChrome.Picker();
        _mode.ItemsSource = vm.CompileModeOptions;
        _mode.SelectedItem = vm.CompileMode;
        _mode.SelectionChanged += (_, _) =>
        {
            if (_mode.SelectedItem is RouterCompileMode mode)
            {
                vm.CompileMode = mode;
            }
        };
        Add(header, _mode, 6);

        content.Children.Add(header);
    }

    protected override void OnNodePropertyChanged(string propertyName)
    {
        if (Node is not EnumSelectorNodeViewModel vm)
        {
            return;
        }

        if (propertyName is nameof(EnumSelectorNodeViewModel.SelectedValue)
            && _method is not null
            && !ReferenceEquals(_method.SelectedItem, vm.SelectedValue))
        {
            _method.SelectedItem = vm.SelectedValue;
        }

        if (propertyName is nameof(EnumSelectorNodeViewModel.LastRouted))
        {
            SetStatus(vm.LastRouted);
        }

        if (propertyName is nameof(EnumSelectorNodeViewModel.ExecutionOrderText))
        {
            SetExecOrder(vm.HasExecutionOrder ? vm.ExecutionOrderText : string.Empty);
        }
    }

    private static void AddFixedRow(Grid grid, double height)
        => grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(height) });

    private static void Add(Grid grid, FrameworkElement child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }
}
