using Demo.ViewModels;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// 「真正在算」的那个节点：标题行（标题 + 执行序号 + 状态胶囊）、一条用途描述、一块可编辑的脚本。
/// 骨架照这套设计（Avalonia PythonNodeView.axaml）：32 / 描述带 / 主体，主体三列 64 / * / 64
/// —— 中间是脚本区（自己一块更深的底），两边那两列是端口名的通道。
/// <para>
/// 端口名由表面画，不在这棵树里：表面才知道端口行落在哪一行（位置由 <see cref="NodePorts"/> 一处给出），
/// 名字与字形因此不可能分家。
/// </para>
/// </summary>
internal sealed class PythonNodeView : NodeViewBase
{
    protected override Color Accent => CardPalette.AccentTimerPython;

    protected override string InitialExecOrder(IWorkflowNodeViewModel node)
        => node is PythonScriptNodeViewModel { HasExecutionOrder: true } vm ? vm.ExecutionOrderText : string.Empty;

    protected override string InitialStatus(IWorkflowNodeViewModel node)
        => (node as PythonScriptNodeViewModel)?.LastStatus ?? string.Empty;

    protected override void Build(IWorkflowNodeViewModel node, Grid content)
    {
        var vm = (PythonScriptNodeViewModel)node;

        // 描述带固定这么高：端口行要让开它，而端口行的位置是表面事先算出来的 ——
        // 两边读的是同一个常量（NodePorts.DescriptorHeight）
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(NodePorts.DescriptorHeight) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

        var description = NodeChrome.Text(vm.Description, 10.5, CardPalette.LabelBrush, FontWeights.Normal);
        description.TextWrapping = TextWrapping.Wrap;
        var descriptionStrip = new Border
        {
            Padding = new Thickness(12, 5),
            // 固定高的描述带 + 自己裁：描述长过两条就在这里截住，不会压到下面的端口行
            ClipToBounds = true,
            Child = description,
        };
        Grid.SetRow(descriptionStrip, 0);
        content.Children.Add(descriptionStrip);

        var descriptionDivider = NodeChrome.Divider();
        Grid.SetRow(descriptionDivider, 0);
        content.Children.Add(descriptionDivider);

        // 主体三列：两个边列是端口名的通道，中间是脚本区
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(NodePorts.PortColumnWidth) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(NodePorts.PortColumnWidth) });

        var code = NodeChrome.CodeBox(out var editor);
        code.Margin = new Thickness(4, 6);
        editor.Text = vm.Script;
        editor.TextChanged += (_, _) => vm.Script = editor.Text ?? string.Empty;
        Grid.SetColumn(code, 1);
        body.Children.Add(code);

        Grid.SetRow(body, 1);
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
            SetStatus(vm.LastStatus);
        }

        if (propertyName is nameof(PythonScriptNodeViewModel.ExecutionOrderText))
        {
            SetExecOrder(vm.HasExecutionOrder ? vm.ExecutionOrderText : string.Empty);
        }
    }
}
