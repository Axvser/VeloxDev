using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.AI;
using VeloxDev.MVVM.Serialization;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();

    public WorkflowView()
    {
        InitializeComponent();
        DataContext = _workflowViewModel;
        InitializeNetworkDemo();
    }

    private async void SelectWorkflow(object sender, RoutedEventArgs e)
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
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var result = json.Deserialize<TreeViewModel>();
                result.Layout = result.Layout.AdaptTo(
                    new VeloxDev.WorkflowSystem.Size(1920, 1080),
                    out double vpX, out double vpY);

                UnsubscribeAutoScroll(_workflowViewModel);
                _workflowViewModel = result;
                DataContext = _workflowViewModel;
                SubscribeAutoScroll(_workflowViewModel);
                WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
                _ = Dispatcher.InvokeAsync(() =>
                {
                    var sv = this.FindName("PART_ScrollViewer") as System.Windows.Controls.ScrollViewer;
                    if (sv is not null)
                    {
                        var offset = _workflowViewModel.Layout.ActualOffset;
                        sv.ScrollToHorizontalOffset(vpX + offset.Horizontal);
                        sv.ScrollToVerticalOffset(vpY + offset.Vertical);
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                MessageBox.Show($"工作流已从 {dialog.FileName} 加载成功。", "加载成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败：{ex.GetType().Name}\n{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveWorkflow(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TreeViewModel tree) return;

        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            tree.SaveCommand.Execute(Path.Combine(dialog.FolderName, "Workflow.json"));
            MessageBox.Show($"Workflow Saved At {dialog.FolderName}", "OK");
        }
    }

    private void LoadNetworkDemo(object sender, RoutedEventArgs e)
    {
        InitializeNetworkDemo();
    }

    private void InitializeNetworkDemo()
    {
        UnsubscribeAutoScroll(_workflowViewModel);
        _workflowViewModel = WorkflowDemoSession.Create().Tree;
        DataContext = _workflowViewModel;
        SubscribeAutoScroll(_workflowViewModel);
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    private void OnSendToAgent(object sender, RoutedEventArgs e)
    {
        var text = AgentInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _workflowViewModel.AskCommand.Execute(text);
        AgentInput!.Text = string.Empty;
    }

    private void OnAgentInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
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

    private void OnAgentToolCalled()
    {
        Dispatcher.InvokeAsync(() => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnVisualRefreshRequested()
    {
        Dispatcher.InvokeAsync(() => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
    {
        var result = await Dispatcher.InvokeAsync<SelectionDialogResult>(() =>
        {
            var prompt = args.Prompt;
            var options = args.Options;
            var isMulti = args.AllowMultiSelect;
            var win = new System.Windows.Window
            {
                Title = isMulti ? "Agent · 请多选" : "Agent · 请选择",
                Width = 460,
                SizeToContent = System.Windows.SizeToContent.Height,
                MaxHeight = 620,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };

            // ── Header ──
            var headerStack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            headerStack.Children.Add(new TextBlock
            {
                Text = isMulti ? "☑️  Agent · 请多选" : "🤖  Agent · 请选择",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
            });
            var header = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16213e")),
                Child = headerStack,
            };

            // ── Options area ──
            var optionStack = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            // Track UI controls for reading state on submit
            List<CheckBox>? checkBoxes = isMulti ? [] : null;
            string? singleChoice = null;

            foreach (var opt in options)
            {
                if (isMulti)
                {
                    var cb = new CheckBox
                    {
                        Content = opt,
                        IsChecked = false,
                        Margin = new Thickness(0, 0, 0, 6),
                        Padding = new Thickness(8, 6, 8, 6),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                        BorderThickness = new Thickness(1),
                        FontSize = 12,
                    };
                    checkBoxes!.Add(cb);
                    optionStack.Children.Add(cb);
                }
                else
                {
                    var captured = opt;
                    var btn = new System.Windows.Controls.Button
                    {
                        Content = opt,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(14, 10, 14, 10),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 6),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                    };
                    btn.Click += (_, _) => { singleChoice = captured; win.Close(); };
                    optionStack.Children.Add(btn);
                }
            }

            // ── Free text input (always shown) ──
            optionStack.Children.Add(new TextBlock
            {
                Text = args.FreeTextPrompt,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#b0b0b0")),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 4),
            });
            var freeTextBox = new TextBox
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                AcceptsReturn = false,
                MinHeight = 30,
            };
            optionStack.Children.Add(freeTextBox);

            // ── Confirm / Cancel buttons ──
            if (isMulti)
            {
                var btnRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                var cancelBtn = new System.Windows.Controls.Button
                {
                    Content = "取消",
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 11,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a2a3e")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                cancelBtn.Click += (_, _) => win.Close();
                btnRow.Children.Add(cancelBtn);

                var confirmBtn = new System.Windows.Controls.Button
                {
                    Content = "✓  确认选择",
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 11,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7ec8ff")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                };
                confirmBtn.Click += (_, _) => win.Close();
                btnRow.Children.Add(confirmBtn);
                optionStack.Children.Add(btnRow);
            }
            else
            {
                var cancelBtn = new System.Windows.Controls.Button
                {
                    Content = "取消（不选择）",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 11,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a2a3e")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                };
                cancelBtn.Click += (_, _) => win.Close();
                optionStack.Children.Add(cancelBtn);
            }

            var scroll = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 420,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = optionStack,
            };

            var root = new StackPanel
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };
            root.Children.Add(header);
            root.Children.Add(scroll);
            win.Content = root;

            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) win.Owner = owner;
            win.ShowDialog();

            // Collect results
            List<string>? selectedOptions = isMulti
                ? checkBoxes!.Where(cb => cb.IsChecked == true).Select(cb => (string)cb.Content).ToList()
                : null;
            var selectedSingle = isMulti ? null : singleChoice;
            var freeText = freeTextBox?.Text?.Trim();
            return new SelectionDialogResult
            {
                SelectedOption = selectedSingle,
                SelectedOptions = selectedOptions,
                FreeTextResponse = string.IsNullOrWhiteSpace(freeText) ? null : freeText,
            };
        });

        args.SelectedOption = result.SelectedOption;
        args.SelectedOptions = result.SelectedOptions;
        args.FreeTextResponse = result.FreeTextResponse;
    }

    /// <summary>
    /// Internal helper to carry selection dialog results out of the Dispatcher call.
    /// </summary>
    private sealed class SelectionDialogResult
    {
        public string? SelectedOption { get; init; }
        public IReadOnlyList<string>? SelectedOptions { get; init; }
        public string? FreeTextResponse { get; init; }
    }

    private async Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
    {
        var result = await Dispatcher.InvokeAsync<AgentConfirmationResult>(() =>
        {
            var operationKey = args.OperationKey;
            var description = args.Description;
            var result = AgentConfirmationResult.Deny;

            var win = new System.Windows.Window
            {
                Title = "Agent · 操作确认",
                Width = 440,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };

            // Header
            var headerStack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            headerStack.Children.Add(new TextBlock
            {
                Text = "⚠️  Agent · 操作确认",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffd166")),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
            });
            headerStack.Children.Add(new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a1f00")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffd166")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = operationKey,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffd166")),
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                },
            });
            var header = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16213e")),
                Child = headerStack,
            };

            // Body
            var bodyStack = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MinWidth = 300 };
            bodyStack.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0")),
                FontSize = 12,
            });

            static System.Windows.Controls.Button MakeBtn(string label, string bg, string fg, string border) =>
                new()
                {
                    Content = label,
                    Padding = new Thickness(16, 9, 16, 9),
                    FontSize = 12,
                    Margin = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)),
                    BorderThickness = new Thickness(1),
                };

            var denyBtn   = MakeBtn("✕  拒绝",            "#3b0000", "#ff6b6b", "#ff6b6b");
            var onceBtn   = MakeBtn("✓  仅同意一次",       "#0f3460", "#7ec8ff", "#7ec8ff");
            var alwaysBtn = MakeBtn("✓✓  本次会话始终同意", "#0d3b1a", "#6bffb8", "#6bffb8");

            denyBtn.Click   += (_, _) => { result = AgentConfirmationResult.Deny;        win.Close(); };
            onceBtn.Click   += (_, _) => { result = AgentConfirmationResult.AllowOnce;   win.Close(); };
            alwaysBtn.Click += (_, _) => { result = AgentConfirmationResult.AllowAlways; win.Close(); };

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };
            btnRow.Children.Add(denyBtn);
            btnRow.Children.Add(onceBtn);
            btnRow.Children.Add(alwaysBtn);
            bodyStack.Children.Add(btnRow);

            var root = new StackPanel
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a2e")),
            };
            root.Children.Add(header);
            root.Children.Add(bodyStack);
            win.Content = root;

            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null) win.Owner = owner;
            win.ShowDialog();
            return result;
        });
        args.Result = result;
    }

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (AgentLogScroller is not { Items.Count: > 0 } lb) return;
            lb.ScrollIntoView(lb.Items[lb.Items.Count - 1]);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd(ExecutionLogScroller);
    }

    private static void ScrollToEnd(ScrollViewer? scroller)
    {
        if (scroller is null) return;
        scroller.ScrollToEnd();
    }
}
