using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();
    private WindowNotificationManager _manager;

    public WorkflowView()
    {
        InitializeComponent();
        DataContext = _workflowViewModel;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3 };

        SubscribeAutoScroll(_workflowViewModel);
        InitializeNetworkDemo();
    }

    private async void SaveWorkflow(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "保存 Workflow.json 到指定的目录中"
            });
        if (folder.Count < 1) return;
        var path = folder[0].TryGetLocalPath();
        if (path is null) return;
        _workflowViewModel.SaveCommand.Execute(Path.Combine(path, "Workflow.json"));
        _manager.Show(new Notification("OK", $"Workflow Saved At {path}"));
    }

    private async void SelectWorkflow(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        var file = await topLevel.StorageProvider.OpenFilePickerAsync(
           new FilePickerOpenOptions
           {
               Title = "从Json文件加载工作流",
               AllowMultiple = false,
               FileTypeFilter = [FilePickerFileTypes.Json]
           });
        if (file.Count < 1) return;
        var path = file[0].TryGetLocalPath();
        await using var value = await file[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await value.CopyToAsync(ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var json = await reader.ReadToEndAsync();
        var success = json.TryDeserialize<TreeViewModel>(out var result);
        if (success && result is not null)
        {
            result.Layout = result.Layout.AdaptTo(
                new VeloxDev.WorkflowSystem.Size(1920, 1080),
                out double vpX, out double vpY);

            UnsubscribeAutoScroll(_workflowViewModel);
            _workflowViewModel = result;
            DataContext = _workflowViewModel;
            SubscribeAutoScroll(_workflowViewModel);
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            Dispatcher.UIThread.Post(() =>
            {
                var sv = this.FindControl<Avalonia.Controls.ScrollViewer>("PART_ScrollViewer");
                if (sv is not null)
                {
                    var offset = _workflowViewModel.Layout.ActualOffset;
                    sv.Offset = new Avalonia.Vector(vpX + offset.Horizontal, vpY + offset.Vertical);
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
            _manager.Show(new Notification("OK", $"Workflow Loaded From {path}"));
        }
    }

    private void LoadNetworkDemo(object? sender, RoutedEventArgs e)
    {
        InitializeNetworkDemo();
        _manager.Show(new Notification("OK", "Workflow demo loaded."));
    }

    private void AddCSharpObject(object? sender, RoutedEventArgs e)
    {
        CSharpObjectDemo.AddNextPipeline(_workflowViewModel);
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        _manager.Show(new Notification(
            "C# Script",
            "Four-stage script pipeline added."));
    }

    private void RunCSharpPipeline(object? sender, RoutedEventArgs e)
    {
        if (CSharpObjectDemo.RunLatestPipeline(_workflowViewModel))
        {
            _manager.Show(new Notification(
                "C# Script",
                "The latest script pipeline is running."));
        }
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

    private void OnSendToAgent(object? sender, RoutedEventArgs e)
    {
        var text = AgentInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _workflowViewModel.AskCommand.Execute(text);
        AgentInput!.Text = string.Empty;
    }

    private void OnAgentInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
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
            // 直接内联：工具始终已注册，handler 在此赋值后即可触发
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

    // ── Agent interaction dialogs ────────────────────────────────────────────

    private async Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var prompt = args.Prompt;
            var options = args.Options;
            var isMulti = args.AllowMultiSelect;

            var dialog = new Window
            {
                Title = isMulti ? "Agent · 请多选" : "Agent · 请选择",
                Width = 440,
                MinHeight = 160,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new SolidColorBrush(Color.Parse("#1a1a2e")),
            };

            // ── Header ────────────────────────────────────────────────────
            var headerPanel = new StackPanel { Spacing = 4, Margin = new Thickness(18, 14) };
            headerPanel.Children.Add(new TextBlock
            {
                Text = isMulti ? "☑️  Agent · 请多选" : "🤖  Agent · 请选择",
                Foreground = new SolidColorBrush(Color.Parse("#7ec8ff")),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush(Color.Parse("#e0e0e0")),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            });
            var header = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#16213e")),
                Child = headerPanel,
            };

            // ── Options ───────────────────────────────────────────────────
            var optionsPanel = new StackPanel { Spacing = 6, Margin = new Thickness(16, 12) };

            List<CheckBox>? checkBoxes = isMulti ? [] : null;
            var freeTextBox = new TextBox
            {
                Background = new SolidColorBrush(Color.Parse("#2d2d2d")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
                Padding = new Thickness(8, 6),
                FontSize = 12,
            };

            foreach (var opt in options)
            {
                if (isMulti)
                {
                    var cb = new CheckBox
                    {
                        Content = opt,
                        Foreground = new SolidColorBrush(Color.Parse("#e0e0e0")),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 2),
                    };
                    checkBoxes!.Add(cb);
                    optionsPanel.Children.Add(cb);
                }
                else
                {
                    var captured = opt;
                    var btn = new Button
                    {
                        Content = opt,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(14, 10),
                        FontSize = 12,
                        Background = new SolidColorBrush(Color.Parse("#0f3460")),
                        Foreground = new SolidColorBrush(Color.Parse("#e0e0e0")),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.Parse("#7ec8ff")),
                        CornerRadius = new CornerRadius(6),
                    };
                    btn.Click += (_, _) =>
                    {
                        args.SelectedOption = captured;
                        args.FreeTextResponse = freeTextBox.Text?.Trim();
                        args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                        dialog.Close();
                    };
                    optionsPanel.Children.Add(btn);
                }
            }

            // ── Free text input (always shown) ───────────────────────────
            optionsPanel.Children.Add(new TextBlock
            {
                Text = args.FreeTextPrompt,
                Foreground = new SolidColorBrush(Color.Parse("#b0b0b0")),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 2),
            });
            optionsPanel.Children.Add(freeTextBox);

            // ── Confirm / Cancel ──────────────────────────────────────────
            if (isMulti)
            {
                var confirmBtn = new Button
                {
                    Content = "✓  确认选择",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(16, 9),
                    FontSize = 12,
                    Margin = new Thickness(0, 8, 0, 0),
                    Background = new SolidColorBrush(Color.Parse("#0f3460")),
                    Foreground = new SolidColorBrush(Color.Parse("#7ec8ff")),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.Parse("#7ec8ff")),
                    CornerRadius = new CornerRadius(6),
                };
                confirmBtn.Click += (_, _) =>
                {
                    args.SelectedOptions = checkBoxes!
                        .Where(cb => cb.IsChecked == true)
                        .Select(cb => (string)cb.Content!)
                        .ToList();
                    args.FreeTextResponse = freeTextBox?.Text?.Trim();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                    dialog.Close();
                };
                optionsPanel.Children.Add(confirmBtn);

                var cancelBtn = new Button
                {
                    Content = "取消",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(14, 8),
                    FontSize = 11,
                    Background = new SolidColorBrush(Color.Parse("#2a2a3e")),
                    Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.Parse("#444444")),
                    CornerRadius = new CornerRadius(6),
                };
                cancelBtn.Click += (_, _) => dialog.Close();
                optionsPanel.Children.Add(cancelBtn);
            }
            else
            {
                var cancelBtn = new Button
                {
                    Content = "取消（不选择）",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(14, 8),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0),
                    Background = new SolidColorBrush(Color.Parse("#2a2a3e")),
                    Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.Parse("#444444")),
                    CornerRadius = new CornerRadius(6),
                };
                cancelBtn.Click += (_, _) => dialog.Close();
                optionsPanel.Children.Add(cancelBtn);
            }

            dialog.Content = new StackPanel
            {
                Background = new SolidColorBrush(Color.Parse("#1a1a2e")),
                Children =
                {
                    header,
                    new ScrollViewer
                    {
                        MaxHeight = 420,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = optionsPanel,
                    },
                },
            };

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is not null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();
        });
        // Note: args properties are set inline in button handlers before dialog.Close()
    }

    private async Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
    {
        var result = AgentConfirmationResult.Deny;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var operationKey = args.OperationKey;
            var description = args.Description;

            var dialog = new Window
            {
                Title = "Agent · 操作确认",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new SolidColorBrush(Color.Parse("#1a1a2e")),
            };

            // ── Header ────────────────────────────────────────────────────
            var headerPanel = new StackPanel { Spacing = 4, Margin = new Thickness(18, 14) };
            headerPanel.Children.Add(new TextBlock
            {
                Text = "⚠️  Agent · 操作确认",
                Foreground = new SolidColorBrush(Color.Parse("#ffd166")),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
            });
            headerPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2a1f00")),
                BorderBrush = new SolidColorBrush(Color.Parse("#ffd166")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = operationKey,
                    Foreground = new SolidColorBrush(Color.Parse("#ffd166")),
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                },
            });
            var header = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#16213e")),
                Child = headerPanel,
            };

            // ── Body ──────────────────────────────────────────────────────
            var bodyPanel = new StackPanel { Spacing = 16, Margin = new Thickness(18, 14) };
            bodyPanel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#e0e0e0")),
                FontSize = 12,
            });

            // ── Buttons ───────────────────────────────────────────────────
            static Button MakeBtn(string label, string bg, string fg, string border) => new()
            {
                Content = label,
                Padding = new Thickness(16, 9),
                FontSize = 12,
                Background = new SolidColorBrush(Color.Parse(bg)),
                Foreground = new SolidColorBrush(Color.Parse(fg)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse(border)),
                CornerRadius = new CornerRadius(6),
            };

            var denyBtn   = MakeBtn("✕  拒绝",            "#3b0000", "#ff6b6b", "#ff6b6b");
            var onceBtn   = MakeBtn("✓  仅同意一次",       "#0f3460", "#7ec8ff", "#7ec8ff");
            var alwaysBtn = MakeBtn("✓✓  本次会话始终同意", "#0d3b1a", "#6bffb8", "#6bffb8");

            denyBtn.Click   += (_, _) => { result = AgentConfirmationResult.Deny;        dialog.Close(); };
            onceBtn.Click   += (_, _) => { result = AgentConfirmationResult.AllowOnce;   dialog.Close(); };
            alwaysBtn.Click += (_, _) => { result = AgentConfirmationResult.AllowAlways; dialog.Close(); };

            bodyPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { denyBtn, onceBtn, alwaysBtn },
            });

            dialog.Content = new StackPanel
            {
                Background = new SolidColorBrush(Color.Parse("#1a1a2e")),
                Children = { header, bodyPanel },
            };

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is not null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();
        });
        args.Result = result;
    }

    private void OnAgentToolCalled()
    {
        Dispatcher.UIThread.Post(() => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this));
    }

    private void OnVisualRefreshRequested()
    {
        Dispatcher.UIThread.Post(() => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this), DispatcherPriority.Background);
    }

    private void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ScrollListToEnd(AgentLogScroller), DispatcherPriority.Background);
    }

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd(ExecutionLogScroller);
    }

    private static void ScrollListToEnd(ListBox? listBox)
    {
        if (listBox is null || listBox.ItemCount == 0) return;
        listBox.ScrollIntoView(listBox.ItemCount - 1);
    }

    private static void ScrollToEnd(ScrollViewer? scroller)
    {
        if (scroller is null) return;
        scroller.Offset = new Vector(scroller.Offset.X, scroller.Extent.Height);
    }
}
