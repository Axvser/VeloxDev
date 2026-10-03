using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives.PopupPositioning;
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
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo;

public partial class WorkflowView : UserControl
{
    private TreeViewModel _workflowViewModel = new();
    private WindowNotificationManager _manager;

    /// <summary>
    /// The panel's tree, built over whichever helper's subsystem is currently showing. Kept as a field
    /// because it has to be detached from the scope it was built on — swapping the workflow replaces the
    /// helper, and a tree left subscribed to the old one would keep rebuilding a panel nobody sees.
    /// </summary>
    private SubAgentTreeViewModel? _subAgentTree;

    /// <summary>
    /// The clock behind the panel's elapsed times. The library owns no timer on purpose — a panel that ticks
    /// and a process that hosts one have different lifetimes — so the demo supplies the one it already has a
    /// dispatcher for. One second is the coarsest rate at which a "分/秒" label still looks alive.
    /// </summary>
    private DispatcherTimer? _subAgentTick;

    /// <summary>
    /// Collapses a burst of "the surface is stale" requests into one refresh.
    /// <para>
    /// An Agent turn makes dozens of tool calls and every one of them asks for a refresh, and a refresh here is
    /// total — so without this the editor spent the whole turn running them back to back, which the user
    /// measured as "very very slowly" (2026-09-27). One per view rather than one per request: the merging is
    /// the point. It stays valid across a session swap, since the refresh resolves the tree at call time.
    /// </para>
    /// </summary>
    private readonly CoalescedRefresh _surfaceRefresh;

    public WorkflowView()
    {
        InitializeComponent();

        // The hop is Background on purpose — the priority this view already used for the agent's own updates —
        // so a burst of them can never get ahead of input.
        _surfaceRefresh = new CoalescedRefresh(
            () => WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this),
            action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background));

        // Keep the canvas-info HUD current on every scroll / viewport change: it reads helper.Viewport,
        // which the surface behaviour refreshes, and subscribes to the model for the rest.
        PART_ScrollViewer.ScrollChanged += (_, _) => InfoOverlay.Refresh();

        DataContext = _workflowViewModel;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3 };

        // 菜单的开合报回 hub：它据此收放 IsSuspended，宿主不必自己记账。
        _linkMenu = this.TryFindResource("WorkflowTreeMenu", out var menuResource) ? menuResource as ContextMenu : null;
        if (_linkMenu is not null)
        {
            _linkMenu.Opened += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Opened, _menuPosition, _menuLink));
            _linkMenu.Closed += (_, _) => _linkInteraction?.Publish(
                new ContextMenuEvent(ContextMenuPhase.Closed, _menuPosition, _menuLink));
        }

        // 换树会重建连线交互中枢（它按树构造），所以每次 DataContext 变化都重新取一次并重订。
        DataContextChanged += (_, _) => WireLinkInteraction();

        SubscribeAutoScroll(_workflowViewModel);
        InitializeNetworkDemo();
        InitializeMcp();
    }

    // ── Link interaction (hover / right-click menu / Delete) ──────────────────
    // 命中、悬停与「按 Delete 删哪条」都在 Core 里裁决（hub 用 LinkInteraction.For 取）。本视图只做宿主
    // 那件事：订 hub 的 ContextMenuRequested，用声明的菜单资源弹出，再把开合报回 hub。

    private LinkInteraction? _linkInteraction;
    private ContextMenu? _linkMenu;
    // 菜单当前针对的那条线：菜单被复用，弹出那一刻再读，条目靠菜单的 DataContext 绑定它。
    private IWorkflowLinkViewModel? _menuLink;
    private Anchor _menuPosition = new();

    private void WireLinkInteraction()
    {
        // hub 按树取：适配器也在同一个 LinkInteraction.For(tree) 上转发，宿主与它拿到的必然是同一个。
        var interaction = DataContext is IWorkflowTreeViewModel tree ? LinkInteraction.For(tree) : null;
        if (ReferenceEquals(interaction, _linkInteraction)) return;

        UnwireLinkInteraction();
        _linkInteraction = interaction;
        if (interaction is null) return;

        interaction.ContextMenuRequested += OnContextMenuRequested;
        interaction.ContextMenuDismissRequested += OnContextMenuDismissRequested;
    }

    private void UnwireLinkInteraction()
    {
        if (_linkInteraction is null) return;

        _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
        _linkInteraction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
        _linkInteraction = null;
    }

    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        // 空白画布没有可操作的对象，不给菜单。
        if (e.Link is null || _linkMenu is null) return;
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        if (this.FindControl<Canvas>("PART_Canvas") is not { } canvas) return;

        // 画布坐标 → 屏幕上的一点：先按适配器那套逆变换（world + ActualOffset）回到画布局部，
        // 再由画布换到本控件（宿主）的坐标 —— 菜单的 PlacementRect 正是相对 PlacementTarget 的局部坐标。
        var screen = WorkflowSurfaceMath.ToScreen(e.Position.Horizontal, e.Position.Vertical, tree.Layout);
        var point = canvas.TranslatePoint(new Point(screen.Horizontal, screen.Vertical), this)
                    ?? new Point(screen.Horizontal, screen.Vertical);

        _menuLink = e.Link;
        _menuPosition = e.Position;

        // 菜单的 DataContext 就是这条连线，条目据此绑定命令。
        _linkMenu.DataContext = e.Link;
        _linkMenu.Placement = PlacementMode.AnchorAndGravity;
        _linkMenu.PlacementAnchor = PopupAnchor.TopLeft;
        _linkMenu.PlacementGravity = PopupGravity.BottomRight;
        _linkMenu.PlacementRect = new Rect(point.X, point.Y, 0, 0);
        _linkMenu.Open(this);
    }

    // 菜单指着的那条线已经不在树上：hub 请宿主收起这份菜单（它收不了宿主的弹窗）。收起照常报 Closed，挂起随之放开。
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        _linkMenu?.Close();
    }

    private void InitializeMcp()
    {
        if (_workflowViewModel.GetHelper() is not AgentHelper helper) return;
        helper.Mcp.WithSynchronizationContext(SynchronizationContext.Current);
        McpStatusPanel.DataContext = helper.Mcp.Status;
        _ = helper.LoadMcpServersAsync();
    }

    private async void OnReloadMcp(object? sender, RoutedEventArgs e)
    {
        if (_workflowViewModel.GetHelper() is AgentHelper helper)
            await helper.LoadMcpServersAsync();
    }

    /// <summary>
    /// Attaches the sub-agent panel to a helper's subsystem.
    /// <para>
    /// Unlike <see cref="AgentHelper.Mcp"/> — a property initializer, and so already there when the view is
    /// constructed — the sub-agent subsystem is built inside <c>ProvideAgent</c>, after the key has been read
    /// and the chat client resolved. So it is looked for at attach time rather than assumed, and a host
    /// without a key simply has no panel.
    /// </para>
    /// </summary>
    private void AttachSubAgents(AgentHelper helper)
    {
        if (helper.SubAgents is not { } scope)
        {
            SubAgentPanel.IsVisible = false;
            return;
        }

        // Built here, on the UI thread, and not in the constructor: the tree captures the context it
        // marshals its rebuilds to, so it has to be born on the thread that will render it.
        _subAgentTree = new SubAgentTreeViewModel(scope);
        SubAgentPanel.DataContext = _subAgentTree;
        SubAgentPanel.IsVisible = true;
        StartSubAgentTick();
    }

    /// <summary>
    /// Starts the elapsed-time clock. It runs for as long as the panel is attached rather than only while a
    /// child is running: a child can be spawned at any moment by an agent that is itself one of the children,
    /// so "nothing is running right now" is not a state this can reliably observe and wake up from.
    /// </summary>
    private void StartSubAgentTick()
    {
        _subAgentTick ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _subAgentTick.Tick -= OnSubAgentTick;
        _subAgentTick.Tick += OnSubAgentTick;
        _subAgentTick.Start();
    }

    private void OnSubAgentTick(object? sender, EventArgs e) => _subAgentTree?.TickElapsed();

    private void StopSubAgentTick()
    {
        if (_subAgentTick is null) return;
        _subAgentTick.Stop();
        _subAgentTick.Tick -= OnSubAgentTick;
    }

    /// <summary>
    /// Detaches the panel. Disposing the tree only unsubscribes it — the children it was showing keep
    /// running, because closing a panel is not a decision about the work the panel described.
    /// </summary>
    private void DetachSubAgents()
    {
        StopSubAgentTick();
        SubAgentPanel.DataContext = null;
        SubAgentPanel.IsVisible = false;
        _subAgentTree?.Dispose();
        _subAgentTree = null;
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
            // The surface restores the saved viewport position by itself when the tree is attached below.
            result.Layout.UpdateCommand.Execute(null);

            UnsubscribeAutoScroll(_workflowViewModel);
            _workflowViewModel = result;
            DataContext = _workflowViewModel;
            SubscribeAutoScroll(_workflowViewModel);
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            _manager.Show(new Notification("OK", $"Workflow Loaded From {path}"));
        }
    }

    private void LoadNetworkDemo(object? sender, RoutedEventArgs e)
    {
        InitializeNetworkDemo();
        _manager.Show(new Notification("OK", "Workflow demo loaded."));
    }

    private void InitializeNetworkDemo()
    {
        UnsubscribeAutoScroll(_workflowViewModel);
        _demo = WorkflowDemoSession.Create();
        _workflowViewModel = _demo.Tree;
        DataContext = _workflowViewModel;
        SubscribeAutoScroll(_workflowViewModel);
        _workflowViewModel.Layout.UpdateCommand.Execute(null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);

        // 一轮跑完，检查点这一轮才写得下来 —— 按钮可不可按跟着它走。
        // 要跳回 UI 线程：命令体是以 ConfigureAwait(false) 等的，Exited 落在池线程上，
        // 在那里写控件的属性要么抛、要么被 RaiseCommandEvent 的 try/catch 吞掉（按钮就永远置灰）。
        _demo.Controller.RunCommand.Exited += _ => Dispatcher.UIThread.Post(RefreshRunControls);
        _demo.Controller.ResumeCommand.Exited += _ => Dispatcher.UIThread.Post(RefreshRunControls);
        RefreshRunControls();
    }

    /// <summary>
    /// The demo session behind the tree on screen — <c>null</c> when the tree came from a file instead of from
    /// <see cref="WorkflowDemoSession.Create"/>. The run controls live on it, not on the tree.
    /// </summary>
    private WorkflowDemoSession? _demo;

    // 门与检查点都在会话上，所以这两件事只有拿得到会话时才可按；换过树（载入文件）就什么都别做。
    private void RefreshRunControls()
    {
        ContinueFromCheckpointButton.IsEnabled = _demo?.HasCheckpoint == true;
        RunGateState.Text = _demo?.Gate.IsPaused == true ? "已暂停" : "空闲";
    }

    private void PauseWorkflow(object? sender, RoutedEventArgs e)
    {
        if (_demo is null) return;
        _demo.Gate.Pause();
        RunGateState.Text = "已暂停：停在下一个节点边界";
    }

    private void ResumeWorkflow(object? sender, RoutedEventArgs e)
    {
        if (_demo is null) return;
        _demo.Gate.Resume();
        RunGateState.Text = "运行中";
    }

    private async void ContinueFromCheckpoint(object? sender, RoutedEventArgs e)
    {
        if (_demo is null) return;
        RunGateState.Text = "从检查点继续…";
        await _demo.Controller.ResumeCommand.ExecuteAsync(null);
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
        vm.ExecutionLog.CollectionChanged += OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            // Inlined directly: the tool is always registered, so the handler fires as soon as it is assigned here
            helper.SelectionHandler = ShowSelectionDialogAsync;
            helper.ConfirmationHandler = ShowConfirmationDialogAsync;
            helper.ToolCalled += OnAgentToolCalled;
            helper.VisualRefreshRequested += OnVisualRefreshRequested;

            AttachSubAgents(helper);
        }
    }

    private void UnsubscribeAutoScroll(TreeViewModel vm)
    {
        vm.ExecutionLog.CollectionChanged -= OnExecutionLogChanged;
        if (vm.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.ToolCalled -= OnAgentToolCalled;
            helper.VisualRefreshRequested -= OnVisualRefreshRequested;
        }

        // Outside the guard on purpose: the panel belongs to this view, so it goes when the view lets go of
        // the tree — whether or not the helper turned out to be an AgentHelper.
        DetachSubAgents();
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
                Text = isMulti ? "Agent · 请多选" : "Agent · 请选择",
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
                    Content = "确认选择",
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
                cancelBtn.Click += (_, _) =>
                {
                    // A typed custom response is still a valid answer even when the user cancels.
                    args.FreeTextResponse = freeTextBox.Text?.Trim();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                    dialog.Close();
                };
                optionsPanel.Children.Add(cancelBtn);
            }
            else
            {
                var submitBtn = new Button
                {
                    Content = "使用输入",
                    IsEnabled = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(14, 8),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0),
                    Background = new SolidColorBrush(Color.Parse("#0f3460")),
                    Foreground = new SolidColorBrush(Color.Parse("#7ec8ff")),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.Parse("#7ec8ff")),
                    CornerRadius = new CornerRadius(6),
                };
                freeTextBox.TextChanged += (_, _) => submitBtn.IsEnabled = !string.IsNullOrWhiteSpace(freeTextBox.Text);
                submitBtn.Click += (_, _) =>
                {
                    args.SelectedOption = null;
                    args.FreeTextResponse = freeTextBox.Text?.Trim();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                    dialog.Close();
                };
                optionsPanel.Children.Add(submitBtn);

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
                cancelBtn.Click += (_, _) =>
                {
                    // A typed custom response is still a valid answer even when the user cancels.
                    args.FreeTextResponse = freeTextBox.Text?.Trim();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(args.FreeTextResponse) ? null : args.FreeTextResponse;
                    dialog.Close();
                };
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
                Text = "Agent · 操作确认",
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

            var denyBtn   = MakeBtn("拒绝",            "#3b0000", "#ff6b6b", "#ff6b6b");
            var onceBtn   = MakeBtn("仅同意一次",       "#0f3460", "#7ec8ff", "#7ec8ff");
            var alwaysBtn = MakeBtn("本次会话始终同意", "#0d3b1a", "#6bffb8", "#6bffb8");

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

    // Both entry points land here — a tool call and an explicit visual-refresh request mean the same thing to
    // the surface, so they share the one coalescer.
    private void OnAgentToolCalled() => _surfaceRefresh.Request();

    private void OnVisualRefreshRequested() => _surfaceRefresh.Request();

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd(ExecutionLogScroller);
    }

    private static void ScrollToEnd(ScrollViewer? scroller)
    {
        if (scroller is null) return;
        scroller.Offset = new Vector(scroller.Offset.X, scroller.Extent.Height);
    }
}
