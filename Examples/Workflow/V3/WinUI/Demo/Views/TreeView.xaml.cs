using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using VeloxDev.Core.Extension;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class TreeView : UserControl
    {
        private TreeViewModel ViewModel = new();

        public TreeView()
        {
            InitializeComponent();
        }

        private async void SaveWorkflow(object sender, RoutedEventArgs e)
        {
            if (DataContext is not TreeViewModel tree) return;

            var folderPicker = new Windows.Storage.Pickers.FolderPicker();

            // 设置文件类型过滤器（WinUI 3需要）
            folderPicker.FileTypeFilter.Add("*");

            // 设置选择器启动位置
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;

            // 对于WinUI 3，需要获取窗口句柄
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();

            if (folder != null)
            {
                string filePath = Path.Combine(folder.Path, "Workflow.json");
                tree.SaveCommand.Execute(filePath);

                // 显示成功消息（WinUI 3方式）
                await ShowMessageAsync("保存成功", $"工作流已保存到：{filePath}");
            }
        }

        private async void SelectWorkflow(object sender, RoutedEventArgs e)
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();

            // 设置文件类型过滤器
            filePicker.FileTypeFilter.Add(".json");
            filePicker.FileTypeFilter.Add("*");

            // 设置选择器属性
            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            filePicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

            // 获取窗口句柄并初始化（WinUI 3必需）
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

            var file = await filePicker.PickSingleFileAsync();

            if (file != null)
            {
                try
                {
                    using var stream = await file.OpenStreamForReadAsync();
                    var (Success, Result) = await WorkflowEx.TryDeserializeFromStreamAsync<TreeViewModel>(stream);

                    if (!Success || Result is null)
                    {
                        await ShowMessageAsync("加载失败", "文件格式不正确或解析失败。", "确定");
                        return;
                    }

                    ViewModel = Result;
                    DataContext = ViewModel;

                    await ShowMessageAsync("加载成功", $"工作流已从 {file.Name} 加载成功。", "确定");
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("错误", $"加载文件失败：{ex.Message}", "确定");
                }
            }
        }

        private void SimulateData(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
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
            ViewModel.GetHelper().CreateNode(node1);
            ViewModel.GetHelper().CreateNode(node2);
            ViewModel.GetHelper().CreateNode(node3);

            // 给 Node 挂载 Slot
            node1.GetHelper().CreateSlot(slot1);
            node1.GetHelper().CreateSlot(slot2);
            node2.GetHelper().CreateSlot(slot3);
            node2.GetHelper().CreateSlot(slot4);
            node3.GetHelper().CreateSlot(slot5);

            // 清理历史栈，避免非法的重做与撤销
            ViewModel.GetHelper().ClearHistory();

            // 使用数据上下文
            DataContext = ViewModel;
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
                XamlRoot = this.XamlRoot // 必需设置XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
