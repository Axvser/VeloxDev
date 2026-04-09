using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Core.Extension;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class TreeView : UserControl
    {
        private TreeViewModel ViewModel = new();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public TreeView()
        {
            InitializeComponent();
            LoadNetworkDemo();
        }

        private async void SaveWorkflow(object sender, RoutedEventArgs e)
        {
            if (DataContext is not TreeViewModel tree) return;

            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            var hwnd = GetActiveWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            folderPicker.FileTypeFilter.Add("*");
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            string filePath = Path.Combine(folder.Path, "Workflow.json");
            tree.SaveCommand.Execute(filePath);
            await ShowMessageAsync("保存成功", $"工作流已保存到：{filePath}");
        }

        private async void SelectWorkflow(object sender, RoutedEventArgs e)
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = GetActiveWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
            }

            filePicker.FileTypeFilter.Add(".json");
            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            filePicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

            var file = await filePicker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                var (success, result) = await ComponentModelEx.TryDeserializeFromStreamAsync<TreeViewModel>(stream);

                if (!success || result is null)
                {
                    await ShowMessageAsync("加载失败", "文件格式不正确或解析失败。", "确定");
                    return;
                }

                ViewModel = result;
                DataContext = ViewModel;
                await ShowMessageAsync("加载成功", $"工作流已从 {file.Name} 加载成功。", "确定");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("错误", $"加载文件失败：{ex.Message}", "确定");
            }
        }

        private IntPtr GetActiveWindowHandle()
        {
            try
            {
                var hwnd = GetActiveWindow();
                if (hwnd != IntPtr.Zero)
                {
                    return hwnd;
                }

                hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    return hwnd;
                }

                return FindDemoWindow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve window handle: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        private static IntPtr FindDemoWindow()
        {
            IntPtr foundHandle = IntPtr.Zero;

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.ToString().Contains("Demo", StringComparison.OrdinalIgnoreCase))
                {
                    foundHandle = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return foundHandle;
        }

        private void SimulateData(object sender, RoutedEventArgs e)
        {
            LoadNetworkDemo();
        }

        private void LoadNetworkDemo()
        {
            ViewModel = CreateNetworkDemo();
            DataContext = ViewModel;
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
                Size = new Size(controllerWidth, controllerHeight + 30),
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

        private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ViewModel?.ResetVirtualLinkCommand.Execute(null);
        }

        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint(canvas).Position;
            var anchor = new Anchor(position.X, position.Y);
            ViewModel?.SetPointerCommand.Execute(anchor);
        }

        private async Task ShowMessageAsync(string title, string message, string primaryButtonText = "确定")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.WrapWholeWords
                        }
                    }
                },
                PrimaryButtonText = primaryButtonText,
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}