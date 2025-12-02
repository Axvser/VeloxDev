using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.Core.Extension;

namespace Demo.Views
{
    public sealed partial class TreeView : UserControl
    {
        private TreeViewModel ViewModel = new();

        // Win32 API 声明
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint GA_ROOT = 0;
        private const uint GA_ROOTOWNER = 1;
        private const uint GA_PARENT = 2;

        public TreeView()
        {
            InitializeComponent();
        }

        private async void SaveWorkflow(object sender, RoutedEventArgs e)
        {
            if (DataContext is not TreeViewModel tree) return;

            var folderPicker = new Windows.Storage.Pickers.FolderPicker();

            // 使用 Win32 API 获取窗口句柄
            var hwnd = GetActiveWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            folderPicker.FileTypeFilter.Add("*");
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;

            var folder = await folderPicker.PickSingleFolderAsync();

            if (folder != null)
            {
                string filePath = Path.Combine(folder.Path, "Workflow.json");
                tree.SaveCommand.Execute(filePath);
                await ShowMessageAsync("保存成功", $"工作流已保存到：{filePath}");
            }
        }

        private async void SelectWorkflow(object sender, RoutedEventArgs e)
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();

            // 使用 Win32 API 获取窗口句柄
            var hwnd = GetActiveWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
            }

            filePicker.FileTypeFilter.Add(".json");
            filePicker.FileTypeFilter.Add("*");
            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            filePicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

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

        // 使用 Win32 API 获取活动窗口句柄
        private IntPtr GetActiveWindowHandle()
        {
            try
            {
                // 方法1：获取当前活动窗口
                IntPtr hwnd = GetActiveWindow();
                if (hwnd != IntPtr.Zero)
                {
                    Debug.WriteLine($"获取到活动窗口句柄: {hwnd}");
                    return hwnd;
                }

                // 方法2：获取前台窗口
                hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    Debug.WriteLine($"获取到前台窗口句柄: {hwnd}");
                    return hwnd;
                }

                // 方法3：通过枚举窗口查找 WinUI 窗口
                hwnd = FindWinUIWindow();
                if (hwnd != IntPtr.Zero)
                {
                    Debug.WriteLine($"通过枚举找到 WinUI 窗口句柄: {hwnd}");
                    return hwnd;
                }

                Debug.WriteLine("无法获取窗口句柄");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取窗口句柄时出错: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        // 枚举窗口查找 WinUI 窗口
        private IntPtr FindWinUIWindow()
        {
            IntPtr foundHandle = IntPtr.Zero;

            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                // 检查窗口是否可见
                if (IsWindowVisible(hWnd))
                {
                    // 获取窗口标题
                    var title = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, title, title.Capacity);

                    // 获取窗口类名
                    var className = new System.Text.StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);

                    // 查找包含应用程序名称的窗口（根据你的应用名调整）
                    if (!string.IsNullOrEmpty(title.ToString()) &&
                        title.ToString().Contains("Demo") || // 你的应用名称
                        className.ToString().Contains("ApplicationFrameWindow") ||
                        className.ToString().Contains("Windows.UI.Core.CoreWindow"))
                    {
                        foundHandle = hWnd;
                        return false; // 停止枚举
                    }
                }
                return true; // 继续枚举
            }, IntPtr.Zero);

            return foundHandle;
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // 更简单的方法：直接使用桌面窗口作为后备
        private IntPtr GetSimpleWindowHandle()
        {
            try
            {
                // 首先尝试活动窗口
                IntPtr hwnd = GetActiveWindow();
                if (hwnd != IntPtr.Zero) return hwnd;

                // 然后尝试前台窗口
                hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero) return hwnd;

                // 最后使用桌面窗口（总是存在）
                return GetDesktopWindow();
            }
            catch
            {
                return GetDesktopWindow();
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
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}