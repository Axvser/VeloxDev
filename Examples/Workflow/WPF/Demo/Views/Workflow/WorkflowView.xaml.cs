using Demo.ViewModels;
using Demo.Workflow;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();

    public WorkflowView()
    {
        InitializeComponent();
        LoadNetworkDemo(this, new System.Windows.RoutedEventArgs());
    }

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
                var (success, result) = await ComponentModelEx.TryDeserializeFromStreamAsync<TreeViewModel>(stream);

                if (!success || result is null)
                {
                    System.Windows.MessageBox.Show("文件格式不正确或解析失败。", "加载失败",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                _workflowViewModel = result;
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

    private void SaveWorkflow(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not TreeViewModel tree) return;

        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            tree.SaveCommand.Execute(Path.Combine(dialog.FolderName, "Workflow.json"));
        }
    }

    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        if (DataContext is not TreeViewModel tree) return;
        var point = e.GetPosition(WorkflowCanvas);
        tree.SetPointerCommand.Execute(new Anchor(
            point.X - tree.Layout.ActualOffset.Horizontal,
            point.Y - tree.Layout.ActualOffset.Vertical,
            0));
    }

    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        tree.VirtualLink.Sender.State &= ~SlotState.PreviewSender;
        tree.ResetVirtualLinkCommand.Execute(null);
    }

    private void LoadNetworkDemo(object sender, System.Windows.RoutedEventArgs e)
    {
        _workflowViewModel = WorkflowDemoSession.Create().Tree;
        DataContext = _workflowViewModel;
    }
}