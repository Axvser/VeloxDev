using Demo.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Extension;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();

    public WorkflowView()
    {
        InitializeComponent();
    }

    // 从指定的json文件加载工作流
    private async void SelectWorkflow(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择工作流文件",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                using var stream = File.OpenRead(dialog.FileName);
                var (Success, Result) = await WorkflowEx.TryDeserializeFromStreamAsync<TreeViewModel>(stream);

                if (!Success || Result is null)
                {
                    System.Windows.MessageBox.Show("文件格式不正确或解析失败。", "加载失败",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                _workflowViewModel = Result;
                DataContext = _workflowViewModel;

                System.Windows.MessageBox.Show($"工作流已从 {dialog.FileName} 加载成功。", "加载成功",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载文件失败：{ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    // 保存工作流到指定的json文件
    private void SaveWorkflow(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not TreeViewModel tree) return;

        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            tree.SaveCommand.Execute(Path.Combine(dialog.FolderName, "Workflow.json"));
        }
    }

    // 更新当前鼠标所处位置
    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        var point = e.GetPosition(this);
        tree.SetPointerCommand.Execute(new Anchor(point.X, point.Y, 0));
    }

    // 重置连接器
    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        tree.GetHelper().ResetVirtualLink();
    }

    // 模拟工作流数据
    private void SimulateData(object sender, System.Windows.RoutedEventArgs e)
    {
        var slot1 = new SlotViewModel()
        {
            Offset = new Offset(170, 60),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneBoth,
        };
        var slot2 = new SlotViewModel()
        {
            Offset = new Offset(170, 120),
            Size = new Size(20, 20),
            Channel = SlotChannel.MultipleTargets,
        };
        var slot3 = new SlotViewModel()
        {
            Offset = new Offset(10, 100),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneBoth,
        };
        var slot4 = new SlotViewModel()
        {
            Offset = new Offset(10, 200),
            Size = new Size(20, 20),
            Channel = SlotChannel.MultipleSources,
        };
        var node1 = new NodeViewModel()
        {
            Size = new Size(200, 200),
            Anchor = new Anchor(50, 50, 1)
        };
        var node2 = new NodeViewModel()
        {
            Size = new Size(300, 300),
            Anchor = new Anchor(250, 250, 1)
        };

        // 控制器节点，仅用于启动、终结
        var node3 = new ControllerViewModel()
        {
            Size = new Size(400, 200),
            Anchor = new Anchor(400, 400, 1)
        };
        var slot5 = new SlotViewModel()
        {
            Offset = new Offset(335, 55),
            Size = new Size(30, 30),
            Channel = SlotChannel.MultipleTargets,
        };

        // 给 Tree 挂载 Node
        _workflowViewModel.GetHelper().CreateNode(node1);
        _workflowViewModel.GetHelper().CreateNode(node2);
        _workflowViewModel.GetHelper().CreateNode(node3);

        // 给 Node 挂载 Slot
        node1.GetHelper().CreateSlot(slot1);
        node1.GetHelper().CreateSlot(slot2);
        node2.GetHelper().CreateSlot(slot3);
        node2.GetHelper().CreateSlot(slot4);
        node3.GetHelper().CreateSlot(slot5);

        // 清理历史栈，避免非法的重做与撤销
        _workflowViewModel.GetHelper().ClearHistory();

        // 使用数据上下文
        DataContext = _workflowViewModel;
    }
}