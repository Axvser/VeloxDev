using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

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
            WorkflowBehaviors.ViewPool.SetTemplateSelector(PART_Canvas, Resources["NodeSelector"] as DataTemplateSelector);
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
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var success = json.TryDeserialize<TreeViewModel>(out var result);

                if (!success || result is null)
                {
                    await ShowMessageAsync("加载失败", "文件格式不正确或解析失败。", "确定");
                    return;
                }

                result.Layout = result.Layout.AdaptTo(
                    new VeloxDev.WorkflowSystem.Size(1920, 1080));

                UnsubscribeAutoScroll(ViewModel);
                ViewModel = result;
                DataContext = ViewModel;
                SubscribeAutoScroll(ViewModel);
                WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
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
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
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
            {
                helper.SelectionHandler = ShowSelectionDialogAsync;
                helper.ConfirmationHandler = ShowConfirmationDialogAsync;
                helper.ToolCalled += OnAgentToolCalled;
                helper.VisualRefreshRequested += OnVisualRefreshRequested;
            }
        }

        private void UnsubscribeAutoScroll(TreeViewModel vm)
        {
            vm.AgentLog.CollectionChanged -= OnAgentLogChanged;
            vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
            if (vm.GetHelper() is AgentHelper helper)
            {
                helper.SelectionHandler = null;
                helper.ConfirmationHandler = null;
                helper.ToolCalled -= OnAgentToolCalled;
                helper.VisualRefreshRequested -= OnVisualRefreshRequested;
            }
        }

        private async Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
        {
            var tcs = new TaskCompletionSource<SelectionDialogResult>();

            DispatcherQueue.TryEnqueue(async () =>
            {
                var prompt = args.Prompt;
                var options = args.Options;
                var isMulti = args.AllowMultiSelect;

                var optionStack = new StackPanel { Spacing = 6 };
                optionStack.Children.Add(new TextBlock
                {
                    Text = prompt,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8),
                });

                // Track controls
                List<CheckBox>? checkBoxes = isMulti ? [] : null;
                string? singleChoice = null;
                var freeTextBox = new TextBox
                {
                    PlaceholderText = args.FreeTextPrompt,
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8),
                };

                foreach (var opt in options)
                {
                    if (isMulti)
                    {
                        var cb = new CheckBox
                        {
                            Content = opt,
                            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4),
                        };
                        checkBoxes!.Add(cb);
                        optionStack.Children.Add(cb);
                    }
                    else
                    {
                        var captured = opt;
                        var btn = new Button
                        {
                            Content = opt,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4),
                        };
                        btn.Click += (_, _) =>
                        {
                            singleChoice = captured;
                            tcs.TrySetResult(new SelectionDialogResult
                            {
                                SelectedOption = singleChoice,
                                FreeTextResponse = freeTextBox.Text?.Trim(),
                            });
                        };
                        optionStack.Children.Add(btn);
                    }
                }

                // ── Free text input (always shown) ──
                optionStack.Children.Add(new TextBlock
                {
                    Text = args.FreeTextPrompt,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.Gray),
                    FontSize = 11,
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 4),
                });
                optionStack.Children.Add(freeTextBox);

                var scroller = new ScrollViewer
                {
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = optionStack,
                };

                if (isMulti)
                {
                    // Multi-select mode: ContentDialog with confirm/cancel
                    var dialog = new ContentDialog
                    {
                        Title = "☑️  Agent · 请多选",
                        PrimaryButtonText = "✓  确认选择",
                        CloseButtonText = "取消",
                        DefaultButton = ContentDialogButton.Primary,
                        Content = scroller,
                        XamlRoot = this.XamlRoot,
                    };
                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        tcs.TrySetResult(new SelectionDialogResult
                        {
                            SelectedOptions = checkBoxes!
                                .Where(cb => cb.IsChecked == true)
                                .Select(cb => (string)cb.Content)
                                .ToList(),
                            FreeTextResponse = freeTextBox?.Text?.Trim(),
                        });
                    }
                    else
                    {
                        tcs.TrySetResult(new SelectionDialogResult());
                    }
                }
                else
                {
                    // Single-select mode: inline buttons call dialog.Hide()
                    ContentDialog dialog = new()
                    {
                        Title = "🤖  Agent · 请选择",
                        PrimaryButtonText = "取消",
                        XamlRoot = this.XamlRoot,
                        DefaultButton = ContentDialogButton.None,
                    };

                    string? chosen = null;

                    foreach (var opt in options)
                    {
                        var captured = opt;
                        var btn = new Button
                        {
                            Content = opt,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4),
                        };
                        btn.Click += (_, _) =>
                        {
                            chosen = captured;
                            tcs.TrySetResult(new SelectionDialogResult
                            {
                                SelectedOption = chosen,
                                FreeTextResponse = freeTextBox?.Text?.Trim(),
                            });
                            dialog.Hide();
                        };
                        optionStack.Children.Add(btn);
                    }

                    dialog.Content = scroller;
                    await dialog.ShowAsync();

                    // If not set yet (dismissed via ESC/backdrop/close btn)
                    tcs.TrySetResult(new SelectionDialogResult
                    {
                        FreeTextResponse = freeTextBox?.Text?.Trim(),
                    });
                }
            });

            var result = await tcs.Task;
            args.SelectedOption = result.SelectedOption;
            args.SelectedOptions = result.SelectedOptions;
            args.FreeTextResponse = string.IsNullOrWhiteSpace(result.FreeTextResponse) ? null : result.FreeTextResponse;
        }

        /// <summary>
        /// Internal helper to carry selection dialog results.
        /// </summary>
        private sealed class SelectionDialogResult
        {
            public string? SelectedOption { get; init; }
            public IReadOnlyList<string>? SelectedOptions { get; init; }
            public string? FreeTextResponse { get; init; }
        }

        private async Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
        {
            var tcs = new TaskCompletionSource<AgentConfirmationResult>();

            DispatcherQueue.TryEnqueue(async () =>
            {
                var result = AgentConfirmationResult.Deny;
                var operationKey = args.OperationKey;
                var description = args.Description;

                var bodyPanel = new StackPanel { Spacing = 8 };
                bodyPanel.Children.Add(new TextBlock
                {
                    Text = operationKey,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Opacity = 0.7,
                });
                bodyPanel.Children.Add(new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.WrapWholeWords,
                });

                var dialog = new ContentDialog
                {
                    Title = "⚠️  Agent · 操作确认",
                    Content = bodyPanel,
                    PrimaryButtonText = "✓  仅同意一次",
                    SecondaryButtonText = "✓✓  本次会话始终同意",
                    CloseButtonText = "✕  拒绝",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                };

                var outcome = await dialog.ShowAsync();
                result = outcome switch
                {
                    ContentDialogResult.Primary   => AgentConfirmationResult.AllowOnce,
                    ContentDialogResult.Secondary => AgentConfirmationResult.AllowAlways,
                    _                             => AgentConfirmationResult.Deny,
                };

                tcs.TrySetResult(result);
            });

            args.Result = await tcs.Task;
        }

        private void OnAgentToolCalled()
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this));
        }

        private void OnVisualRefreshRequested()
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this));
        }

        private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (AgentLogScroller is { Items.Count: > 0 } lv)
                    lv.ScrollIntoView(lv.Items[lv.Items.Count - 1]);
            });
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
