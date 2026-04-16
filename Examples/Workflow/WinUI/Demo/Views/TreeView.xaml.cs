using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

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
            InitializeNetworkDemo();
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

                UnsubscribeAutoScroll(ViewModel);
                ViewModel = result;
                DataContext = ViewModel;
                SubscribeAutoScroll(ViewModel);
                await ShowMessageAsync("加载成功", $"工作流已从 {file.Name} 加载成功。", "确定");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("错误", $"加载文件失败：{ex.Message}", "确定");
            }
        }

        private void LoadNetworkDemo(object sender, RoutedEventArgs e)
        {
            InitializeNetworkDemo();
        }

        private void InitializeNetworkDemo()
        {
            UnsubscribeAutoScroll(ViewModel);
            ViewModel = WorkflowDemoSession.Create().Tree;
            DataContext = ViewModel;
            SubscribeAutoScroll(ViewModel);
            ViewModel.Layout.UpdateCommand.Execute(null);
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

        private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            ViewModel.VirtualLink.Sender.State &= ~SlotState.PreviewSender;
            ViewModel.ResetVirtualLinkCommand.Execute(null);
        }

        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint(canvas).Position;
            var anchor = new Anchor(
                position.X - ViewModel.Layout.ActualOffset.Horizontal,
                position.Y - ViewModel.Layout.ActualOffset.Vertical,
                0);
            ViewModel.SetPointerCommand.Execute(anchor);
        }

        private void OnSendToAgent(object sender, RoutedEventArgs e)
        {
            var text = AgentInput?.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            ViewModel.AskCommand.Execute(text);
            AgentInput!.Text = string.Empty;
        }

        private void OnAgentInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                OnSendToAgent(sender, e);
                e.Handled = true;
            }
        }

        private void SubscribeAutoScroll(TreeViewModel vm)
        {
            vm.AgentLog.CollectionChanged += OnAgentLogChanged;
            vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
            if (vm.GetHelper() is AgentHelper helper)
                helper.ToolCalled += OnAgentToolCalled;
        }

        private void UnsubscribeAutoScroll(TreeViewModel vm)
        {
            vm.AgentLog.CollectionChanged -= OnAgentLogChanged;
            vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
            if (vm.GetHelper() is AgentHelper helper)
                helper.ToolCalled -= OnAgentToolCalled;
        }

        private void OnAgentToolCalled()
        {
        }

        private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScrollToEnd(AgentLogScroller);
        }

        private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScrollToEnd(ExecutionLogScroller);
        }

        private static void ScrollToEnd(ScrollViewer? scroller)
        {
            if (scroller is null) return;
            scroller.ChangeView(null, scroller.ScrollableHeight, null);
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
