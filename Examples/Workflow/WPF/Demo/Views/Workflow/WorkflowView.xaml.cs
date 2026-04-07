using Demo.ViewModels;
using Microsoft.Win32;
using System;
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
        LoadNetworkDemo();
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
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        var point = e.GetPosition((System.Windows.IInputElement)sender);
        tree.SetPointerCommand.Execute(new Anchor(point.X, point.Y, 0));
    }

    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        tree.GetHelper().ResetVirtualLink();
    }

    private void SimulateData(object sender, System.Windows.RoutedEventArgs e)
    {
        LoadNetworkDemo();
    }

    private void LoadNetworkDemo()
    {
        _workflowViewModel = CreateNetworkDemo();
        DataContext = _workflowViewModel;
    }

    private static TreeViewModel CreateNetworkDemo()
    {
        var tree = new TreeViewModel();
        var helper = tree.GetHelper();
        const double nodeWidth = 220;
        const double nodeHeight = 180;
        const double controllerWidth = 240;
        const double controllerHeight = 170;

        NodeViewModel CreateNode(
            string title,
            NetworkRequestMethod method,
            string url,
            string captureKey,
            double left,
            double top,
            string headers = "",
            string bodyTemplate = "")
            => new()
            {
                Title = title,
                Method = method,
                Url = url,
                Headers = headers,
                BodyTemplate = bodyTemplate,
                CaptureKey = captureKey,
                Size = new Size(nodeWidth, nodeHeight),
                Anchor = new Anchor(left, top, 1),
            };

        var controller = new ControllerViewModel
        {
            Size = new Size(controllerWidth, controllerHeight),
            Anchor = new Anchor(140, 220, 1),
            SeedPayload = "demo-request-chain",
            BroadcastMode = WorkflowBroadcastMode.BreadthFirst,
        };

        var fetchTodo = CreateNode("Fetch Todo", NetworkRequestMethod.Get, "https://jsonplaceholder.typicode.com/todos/1", "todo", 420, 60);
        var fetchPost = CreateNode("Fetch Post", NetworkRequestMethod.Get, "https://jsonplaceholder.typicode.com/posts/1", "post", 420, 300);
        var auditTodo = CreateNode("Audit Todo", NetworkRequestMethod.Post, "https://httpbin.org/post", "audit", 710, 20, headers: "X-Demo-Source: VeloxDev Workflow", bodyTemplate: "{\"todoSummary\":\"{{todo.summary}}\",\"todoStatus\":\"{{todo.status}}\"}");
        var patchRemote = CreateNode("Patch Remote", NetworkRequestMethod.Patch, "https://httpbin.org/patch", "patch", 710, 180, bodyTemplate: "{\"todoUrl\":\"{{todo.url}}\",\"status\":\"processed\"}");
        var syncPost = CreateNode("Sync Post", NetworkRequestMethod.Post, "https://httpbin.org/post", "sync", 710, 340, bodyTemplate: "{\"postUrl\":\"{{post.url}}\",\"summary\":\"{{post.summary}}\"}");
        var deleteRemote = CreateNode("Delete Remote", NetworkRequestMethod.Delete, "https://httpbin.org/delete", "delete", 710, 500, headers: "X-Delete-Reason: {{todo.status}}");
        var archiveTrace = CreateNode("Archive Trace", NetworkRequestMethod.Post, "https://httpbin.org/post", "archive", 1000, 220, bodyTemplate: "{\"last\":\"{{last.summary}}\",\"seed\":\"{{seed}}\"}");

        foreach (var node in new IWorkflowNodeViewModel[]
        {
            controller,
            fetchTodo,
            fetchPost,
            auditTodo,
            patchRemote,
            syncPost,
            deleteRemote,
            archiveTrace,
        })
        {
            helper.CreateNode(node);
        }

        controller.OutputSlot = CreateOutputSlot(controllerWidth, controllerHeight, SlotChannel.MultipleTargets);
        fetchTodo.InputSlot = CreateInputSlot(nodeHeight);
        fetchTodo.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.MultipleTargets);
        fetchPost.InputSlot = CreateInputSlot(nodeHeight);
        fetchPost.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.MultipleTargets);
        auditTodo.InputSlot = CreateInputSlot(nodeHeight);
        auditTodo.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        patchRemote.InputSlot = CreateInputSlot(nodeHeight);
        patchRemote.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        syncPost.InputSlot = CreateInputSlot(nodeHeight);
        syncPost.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        deleteRemote.InputSlot = CreateInputSlot(nodeHeight);
        deleteRemote.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        archiveTrace.InputSlot = CreateInputSlot(nodeHeight, SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, fetchTodo.InputSlot!);
        Connect(tree, controller.OutputSlot!, fetchPost.InputSlot!);
        Connect(tree, fetchTodo.OutputSlot!, auditTodo.InputSlot!);
        Connect(tree, fetchTodo.OutputSlot!, patchRemote.InputSlot!);
        Connect(tree, fetchPost.OutputSlot!, syncPost.InputSlot!);
        Connect(tree, fetchPost.OutputSlot!, deleteRemote.InputSlot!);
        Connect(tree, auditTodo.OutputSlot!, archiveTrace.InputSlot!);
        Connect(tree, patchRemote.OutputSlot!, archiveTrace.InputSlot!);
        Connect(tree, syncPost.OutputSlot!, archiveTrace.InputSlot!);
        Connect(tree, deleteRemote.OutputSlot!, archiveTrace.InputSlot!);

        helper.ClearHistory();
        return tree;
    }

    private static SlotViewModel CreateInputSlot(double nodeHeight, SlotChannel channel = SlotChannel.OneSource)
        => new()
        {
            Offset = new Offset(0, (nodeHeight - 20) / 2),
            Size = new Size(20, 20),
            Channel = channel,
        };

    private static SlotViewModel CreateOutputSlot(double nodeWidth, double nodeHeight, SlotChannel channel)
        => new()
        {
            Offset = new Offset(nodeWidth - 20, (nodeHeight - 20) / 2),
            Size = new Size(20, 20),
            Channel = channel,
        };

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}