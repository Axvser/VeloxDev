using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Input;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

public partial class WorkflowView : ContentView
{
    private TreeViewModel _workflowViewModel = new();
    private bool _layoutRefreshPending;
    private Page MainPage => Application.Current?.Windows[0].Page ?? throw new InvalidOperationException();

    // 右键菜单挂在树的 hub 上；换会话/换树时先解绑，旧 hub 才不会继续握着这个控件的委托。
    private LinkInteraction? _interaction;

    // 当前弹出层作用的那条连线；没有弹出层时为 null。也是 ContextMenuDismissRequested 的判据：
    // 只有指着同一条线的菜单才会被收起。
    private IWorkflowLinkViewModel? _menuLink;

#if WINDOWS
    // Windows 上正在弹的原生 flyout；hub 收不了它，由宿主 Hide，并在它的 Closed 里清掉。
    private Microsoft.UI.Xaml.Controls.MenuFlyout? _openFlyout;
#endif

    public WorkflowView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.Scrolled += (_, _) => InfoOverlay.Update();
        PART_ScrollViewer.SizeChanged += (_, _) => InfoOverlay.Update();
    }

    private void LoadNetworkDemo()
    {
        var session = WorkflowDemoSession.Create();
        Session = session;
    }

    private async void OnSelectClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择工作流文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, [".json"] },
                    { DevicePlatform.Android, ["application/json"] },
                    { DevicePlatform.iOS, ["public.json"] },
                    { DevicePlatform.MacCatalyst, ["public.json"] },
                }),
            });

            if (result is null) return;

            using var stream = await result.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var success = json.TryDeserialize<TreeViewModel>(out var tree);

            if (!success || tree is null)
            {
                await MainPage.DisplayAlertAsync("Load Failed", "The file format is invalid or could not be parsed.", "OK");
                return;
            }

            tree.Layout.UpdateCommand.Execute(null);

            var session = WorkflowDemoSession.FromTree(tree);
            Session = session;
        }
        catch (Exception ex)
        {
            await MainPage.DisplayAlertAsync("Error", $"Failed to load file: {ex.Message}", "OK");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        try
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, "Workflow.json");
            if (_workflowViewModel.SaveCommand is ICommand cmd)
                cmd.Execute(filePath);
            await MainPage.DisplayAlertAsync("Save Succeeded", $"Workflow saved to: {filePath}", "OK");
        }
        catch (Exception ex)
        {
            await MainPage.DisplayAlertAsync("Error", $"Failed to save file: {ex.Message}", "OK");
        }
    }

    private void OnLoadNetworkDemoClicked(object? sender, EventArgs e)
    {
        LoadNetworkDemo();
    }

    // ── Run controls ────────────────────────────────────────────────────────

    // The gate and the checkpoint live on the session, not on the tree, so these controls are only
    // usable when one is attached — and with none there is nothing to hold or to carry on.
    private void RefreshRunControls()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ContinueFromCheckpointButton.IsEnabled = Session?.HasCheckpoint == true;
            RunGateState.Text = Session?.Gate.IsPaused == true ? "已暂停" : "空闲";
        });
    }

    private void OnRunCommandExited(CommandEventArgs e) => RefreshRunControls();

    private void OnPauseClicked(object? sender, EventArgs e)
    {
        if (Session is not { } session) return;
        session.Gate.Pause();
        RunGateState.Text = "已暂停：停在下一个节点边界";
    }

    private void OnResumeClicked(object? sender, EventArgs e)
    {
        if (Session is not { } session) return;
        session.Gate.Resume();
        RunGateState.Text = "运行中";
    }

    private async void OnContinueFromCheckpointClicked(object? sender, EventArgs e)
    {
        if (Session is not { } session) return;
        RunGateState.Text = "从检查点继续…";
        await session.Controller.ResumeCommand.ExecuteAsync(null);
    }

    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session),
        typeof(WorkflowDemoSession),
        typeof(WorkflowView),
        null,
        propertyChanged: OnSessionChanged);

    public WorkflowDemoSession? Session
    {
        get => (WorkflowDemoSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// View-model collection fed to the canvas <c>ViewPool</c>. Mirrors
    /// <see cref="IWorkflowTreeViewModelHelper.VisibleItems"/> but drops link view
    /// models — links are rendered by the shared link overlay, so the
    /// pool must not materialize one GraphicsView per link.
    /// </summary>
    public static readonly BindableProperty NodeItemsSourceProperty = BindableProperty.Create(
        nameof(NodeItemsSource),
        typeof(INotifyCollectionChanged),
        typeof(WorkflowView),
        null);

    public INotifyCollectionChanged? NodeItemsSource
    {
        get => (INotifyCollectionChanged?)GetValue(NodeItemsSourceProperty);
        set => SetValue(NodeItemsSourceProperty, value);
    }

    private void UpdateNodeItemsSource(TreeViewModel? tree)
    {
        if (NodeItemsSource is NodeOnlyVisibleItems wrapper)
        {
            wrapper.Detach();
        }

        var visible = tree?.GetHelper()?.VisibleItems;
        NodeItemsSource = visible is null ? null : new NodeOnlyVisibleItems(visible);
    }

    private static void OnSessionChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (WorkflowView)bindable;
        view.AttachSession((WorkflowDemoSession?)oldValue, (WorkflowDemoSession?)newValue);
    }

    private void AttachSession(WorkflowDemoSession? oldSession, WorkflowDemoSession? newSession)
    {
        if (oldSession is not null)
        {
            UnsubscribeAutoScroll(oldSession.Tree);
            oldSession.Controller.RunCommand.Exited -= OnRunCommandExited;
            oldSession.Controller.ResumeCommand.Exited -= OnRunCommandExited;
        }

        _workflowViewModel = newSession?.Tree ?? new TreeViewModel();
        // MAUI propagates BindingContext through the visual tree automatically,
        // so setting it on the ContentView root is sufficient. Do NOT set
        // BindingContext on individual child elements — that breaks the natural
        // inheritance chain and can cause missed binding updates.
        BindingContext = _workflowViewModel;
        UpdateNodeItemsSource(newSession?.Tree);
        UpdateInteraction(newSession?.Tree);
        // Propagate the tree to the HUD explicitly so its BindingContextChanged fires even if
        // inheritance does not reach the nested overlay.
        InfoOverlay.BindingContext = _workflowViewModel;

        if (newSession is not null)
        {
            SubscribeAutoScroll(newSession.Tree);
            if (newSession.Tree.GetHelper() is AgentHelper helper)
            {
                helper.Mcp.WithSynchronizationContext(SynchronizationContext.Current);
                _ = helper.LoadMcpServersAsync();
            }
            newSession.Tree.Layout.UpdateCommand.Execute(null);

            // A run only writes its checkpoint on the way out, so the continue button's availability
            // follows the run's exits rather than anything the tree raises.
            newSession.Controller.RunCommand.Exited += OnRunCommandExited;
            newSession.Controller.ResumeCommand.Exited += OnRunCommandExited;
        }

        RefreshRunControls();
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

    private async void OnAgentLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            await Task.Yield();
            if (_workflowViewModel.AgentLog is { Count: > 0 } log)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { AgentLogScroller?.ScrollTo(log.Count - 1, position: ScrollToPosition.End, animate: false); }
                    catch { /* UI exceptions from auto-scroll are non-fatal */ }
                });
            }
        }
        catch
        {
            // Swallow cross-thread setup exceptions.
        }
    }

    private Task ShowSelectionDialogAsync(AgentSelectionEventArgs args)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var isMulti = args.AllowMultiSelect;

            var page = new ContentPage
            {
                BackgroundColor = Color.FromArgb("#1a1a2e"),
                Title = isMulti ? "Agent · 请多选" : "Agent · 请选择",
            };

            var stack = new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(20),
            };

            var promptLabel = new Label
            {
                Text = args.Prompt,
                TextColor = Color.FromArgb("#e0e0e0"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8),
            };
            stack.Children.Add(promptLabel);

            List<CheckBox>? checkBoxes = isMulti ? [] : null;
            var freeTextEntry = new Entry
            {
                BackgroundColor = Color.FromArgb("#2d2d2d"),
                TextColor = Colors.White,
                Placeholder = "（可选）",
                PlaceholderColor = Color.FromArgb("#666666"),
            };

            foreach (var opt in args.Options)
            {
                if (isMulti)
                {
                    var cb = new CheckBox { Color = Color.FromArgb("#7ec8ff") };
                    var label = new Label { Text = opt, TextColor = Color.FromArgb("#e0e0e0"), FontSize = 13, VerticalOptions = LayoutOptions.Center };
                    var row = new HorizontalStackLayout { Spacing = 8 };
                    row.Children.Add(cb);
                    row.Children.Add(label);
                    checkBoxes!.Add(cb);
                    stack.Children.Add(row);
                }
                else
                {
                    var captured = opt;
                    var btn = new Button
                    {
                        Text = opt,
                        BackgroundColor = Color.FromArgb("#0f3460"),
                        TextColor = Color.FromArgb("#e0e0e0"),
                        BorderColor = Color.FromArgb("#7ec8ff"),
                        BorderWidth = 1, CornerRadius = 6, HeightRequest = 40,
                        HorizontalOptions = LayoutOptions.Fill,
                    };
                    btn.Clicked += (_, _) =>
                    {
                        args.SelectedOption = captured;
                        args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                        _ = MainPage.Navigation.PopModalAsync(true);
                    };
                    stack.Children.Add(btn);
                }
            }

            stack.Children.Add(new Label { Text = args.FreeTextPrompt, TextColor = Color.FromArgb("#b0b0b0"), FontSize = 11, Margin = new Thickness(0, 6, 0, 2) });
            stack.Children.Add(freeTextEntry);

            if (isMulti)
            {
                var confirmBtn = new Button
                {
                    Text = "确认选择",
                    BackgroundColor = Color.FromArgb("#0f3460"),
                    TextColor = Color.FromArgb("#7ec8ff"),
                    BorderColor = Color.FromArgb("#7ec8ff"),
                    BorderWidth = 1, CornerRadius = 6, HeightRequest = 40, Margin = new Thickness(0, 10, 0, 0),
                };
                confirmBtn.Clicked += (_, _) =>
                {
                    args.SelectedOptions = checkBoxes!.Where(cb => cb.IsChecked)
                        .Select(cb => (string)((Label)((HorizontalStackLayout)cb.Parent).Children[1]).Text).ToList();
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                    _ = MainPage.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(confirmBtn);

                var cancelBtn = new Button { Text = "取消", BackgroundColor = Color.FromArgb("#2a2a3e"), TextColor = Color.FromArgb("#888888"), BorderColor = Color.FromArgb("#444444"), BorderWidth = 1, CornerRadius = 6, HeightRequest = 36 };
                cancelBtn.Clicked += (_, _) => _ = MainPage.Navigation.PopModalAsync(true);
                stack.Children.Add(cancelBtn);
            }
            else
            {
                var submitBtn = new Button { Text = "使用输入", IsEnabled = false, BackgroundColor = Color.FromArgb("#0f3460"), TextColor = Color.FromArgb("#7ec8ff"), BorderColor = Color.FromArgb("#7ec8ff"), BorderWidth = 1, CornerRadius = 6, HeightRequest = 36, Margin = new Thickness(0, 8, 0, 0) };
                freeTextEntry.TextChanged += (_, _) => submitBtn.IsEnabled = !string.IsNullOrWhiteSpace(freeTextEntry.Text);
                submitBtn.Clicked += (_, _) =>
                {
                    args.SelectedOption = null;
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                    _ = MainPage.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(submitBtn);

                var cancelBtn = new Button { Text = "取消（不选择）", BackgroundColor = Color.FromArgb("#2a2a3e"), TextColor = Color.FromArgb("#888888"), BorderColor = Color.FromArgb("#444444"), BorderWidth = 1, CornerRadius = 6, HeightRequest = 36, Margin = new Thickness(0, 8, 0, 0) };
                cancelBtn.Clicked += (_, _) =>
                {
                    args.SelectedOption = null;
                    args.FreeTextResponse = string.IsNullOrWhiteSpace(freeTextEntry.Text?.Trim()) ? null : freeTextEntry.Text.Trim();
                    _ = MainPage.Navigation.PopModalAsync(true);
                };
                stack.Children.Add(cancelBtn);
            }

            page.Content = new ScrollView { Content = stack };
            await MainPage.Navigation.PushModalAsync(page, true);
        });

    private Task ShowConfirmationDialogAsync(AgentConfirmationEventArgs args)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var allow = await MainPage.DisplayAlertAsync(
                "Agent · 操作确认", $"[Operation] {args.OperationKey}\n\n{args.Description}", "允许", "拒绝");

            if (!allow) { args.Result = AgentConfirmationResult.Deny; return; }

            args.Result = await MainPage.DisplayAlertAsync(
                "Agent · 授权范围", "是否在本次会话中始终允许该操作？", "始终允许", "仅同意一次")
                ? AgentConfirmationResult.AllowAlways : AgentConfirmationResult.AllowOnce;
        });

    private void OnAgentToolCalled() => ScheduleRefresh();
    private void OnVisualRefreshRequested() => ScheduleRefresh();

    private void OnExecutionLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRefresh();

    /// <summary>
    /// Debounces layout refreshes to avoid flooding MAUI's layout system.
    /// MAUI layout passes are expensive — batch them.
    /// </summary>
    private void ScheduleRefresh()
    {
        if (_layoutRefreshPending) return;
        _layoutRefreshPending = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                _layoutRefreshPending = false;
                WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            }
            catch
            {
                // Swallow layout refresh exceptions during active processing.
            }
        });
    }

    // ── Agent Chat ──────────────────────────────────────────────────────────

    private async void OnReloadMcp(object? sender, EventArgs e)
    {
        if (_workflowViewModel.GetHelper() is AgentHelper helper)
            await helper.LoadMcpServersAsync();
    }

    private async void OnSendToAgent(object? sender, EventArgs e)
    {
        var text = AgentInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        AgentInput!.Text = string.Empty;

        try
        {
            if (_workflowViewModel.AskCommand is IVeloxCommand cmd)
                await cmd.ExecuteAsync(text);
            else
                _workflowViewModel.AskCommand.Execute(text);
        }
        catch (Exception ex)
        {
            _workflowViewModel.AppendAgentLog($"[Error] Send failed: {ex.Message}");
        }
    }

    private void OnAgentInputCompleted(object? sender, EventArgs e)
    {
        OnSendToAgent(sender, e);
    }

    // ── Link context menu ───────────────────────────────────────────────────

    // 一棵树一个 hub：换会话时旧的退订、新的订阅。空白画布（Link 为 null）不弹菜单。
    private void UpdateInteraction(TreeViewModel? tree)
    {
        if (_interaction is not null)
        {
            _interaction.ContextMenuRequested -= OnContextMenuRequested;
            _interaction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;
            _interaction = null;
        }

        if (tree is not null)
        {
            _interaction = LinkInteraction.For(tree);
            _interaction.ContextMenuRequested += OnContextMenuRequested;
            _interaction.ContextMenuDismissRequested += OnContextMenuDismissRequested;
        }
    }

    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (e.Link is null)
        {
            return;
        }

        ShowLinkMenu(e.Link, e.Position);
    }

    // 条目自带 Command 就用它（绑定上下文是那条连线，弹出前设好）；没有就落到默认动作：删掉这条连线。
    private static void RunItem(MenuFlyoutItem item, IWorkflowLinkViewModel link)
    {
        if (item.Command is { } command)
        {
            var parameter = item.CommandParameter ?? link;
            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }

            return;
        }

        if (link.DeleteCommand.CanExecute(null))
        {
            link.DeleteCommand.Execute(null);
        }
    }

#if WINDOWS
    // 菜单条目在 XAML 的 LinkContextMenu 里声明；这里只管定位与弹出。
    // 画布坐标 → 视口像素：px = Ruler + 锚点 + 内容偏移 − 滚动偏移（与链接层绘制/命中共用同一条换算）。
    private void ShowLinkMenu(IWorkflowLinkViewModel link, Anchor position)
    {
        if (PART_GridDecorator.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement host)
        {
            return;
        }

        var ruler = Math.Max(0d, PART_GridDecorator.RulerThickness);
        var x = ruler + position.Horizontal + PART_GridDecorator.ContentOffsetX - PART_GridDecorator.ScrollOffsetX;
        var y = ruler + position.Vertical + PART_GridDecorator.ContentOffsetY - PART_GridDecorator.ScrollOffsetY;

        var flyout = BuildPlatformMenu((MenuFlyout)Resources["LinkContextMenu"], link);

        _menuLink = link;
        _openFlyout = flyout;

        // 开合报回 hub：菜单开着时指针飞到菜单上，也不该清掉这次选中的连线。
        flyout.Closed += (_, _) =>
        {
            _openFlyout = null;
            _menuLink = null;
            _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, position, link));
        };
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, position, link));

        flyout.ShowAt(host, new Windows.Foundation.Point(x, y));
    }

    // 菜单指着的那条线已经离开树：hub 收不了原生 flyout，由宿主 Hide。收起照常报 Closed，挂起随之放开。
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        _openFlyout?.Hide();
    }

    // MAUI 没有能在指定点弹出的跨平台菜单；只有 Windows 的原生 MenuFlyout 能做到，所以把声明的条目翻成它。
    private static Microsoft.UI.Xaml.Controls.MenuFlyout BuildPlatformMenu(MenuFlyout declared, IWorkflowLinkViewModel link)
    {
        var flyout = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        foreach (var element in declared)
        {
            switch (element)
            {
                // MenuFlyoutSeparator derives from MenuFlyoutItem, so it must be matched first.
                case MenuFlyoutSeparator:
                    flyout.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
                    break;

                case MenuFlyoutItem item:
                    item.BindingContext = link;
                    var native = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                    {
                        Text = item.Text,
                        IsEnabled = item.IsEnabled,
                    };
                    native.Click += (_, _) => RunItem(item, link);
                    flyout.Items.Add(native);
                    break;
            }
        }

        return flyout;
    }
#else
    // 非 Windows 没有能在指定点弹出的跨平台菜单，所以把声明的条目填进 PART_LinkMenuLayer 这个浮层里，
    // 落在长按处。长按本身由链接层翻译成右键交给 hub；这里只负责呈现。
    private void ShowLinkMenu(IWorkflowLinkViewModel link, Anchor position)
    {
        _menuLink = link;

        var declared = (MenuFlyout)Resources["LinkContextMenu"];
        PART_LinkMenuItems.Children.Clear();
        foreach (var element in declared)
        {
            switch (element)
            {
                case MenuFlyoutSeparator:
                    PART_LinkMenuItems.Children.Add(new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#40FFFFFF"),
                        Margin = new Thickness(6, 2),
                    });
                    break;

                case MenuFlyoutItem item:
                    item.BindingContext = link;
                    var button = new Button
                    {
                        Text = item.Text,
                        IsEnabled = item.IsEnabled,
                        BackgroundColor = Colors.Transparent,
                        TextColor = Colors.White,
                        HeightRequest = 36,
                        Padding = new Thickness(12, 0),
                        HorizontalOptions = LayoutOptions.Fill,
                    };
                    var captured = item;
                    button.Clicked += (_, _) => SelectMenuItem(captured);
                    PART_LinkMenuItems.Children.Add(button);
                    break;
            }
        }

        var ruler = Math.Max(0d, PART_GridDecorator.RulerThickness);
        var x = ruler + position.Horizontal + PART_GridDecorator.ContentOffsetX - PART_GridDecorator.ScrollOffsetX;
        var y = ruler + position.Vertical + PART_GridDecorator.ContentOffsetY - PART_GridDecorator.ScrollOffsetY;

        PART_LinkMenuHost.Margin = new Thickness(Math.Max(0d, x), Math.Max(0d, y), 0, 0);
        PART_LinkMenuLayer.IsVisible = true;
        _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, position, link));
    }

    private void SelectMenuItem(MenuFlyoutItem item)
    {
        var link = _menuLink;
        DismissLinkMenu();
        if (link is not null)
        {
            RunItem(item, link);
        }
    }

    // 菜单指着的那条线已经离开树：hub 收不了这个浮层，由宿主收起（收起照常报 Closed，挂起随之放开）。
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        DismissLinkMenu();
    }
#endif

    // 收起弹出层，并告诉 hub 菜单已经关掉。关闭时位置没有意义。
    private void DismissLinkMenu()
    {
        if (!PART_LinkMenuLayer.IsVisible)
        {
            return;
        }

        PART_LinkMenuLayer.IsVisible = false;
        var link = _menuLink;
        _menuLink = null;
        if (link is not null)
        {
            _interaction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, new Anchor(), link));
        }
    }

    private void OnLinkMenuScrimTapped(object? sender, TappedEventArgs e) => DismissLinkMenu();

    /// <summary>
    /// Mirrors <see cref="IWorkflowTreeViewModelHelper.VisibleItems"/> but drops link
    /// view models, so the node ViewPool only ever materializes node views. Links are
    /// rendered by the shared link overlay instead of one GraphicsView per link.
    /// </summary>
    private sealed class NodeOnlyVisibleItems : ObservableCollection<IWorkflowViewModel>
    {
        private readonly ObservableCollection<IWorkflowViewModel> _source;

        public NodeOnlyVisibleItems(ObservableCollection<IWorkflowViewModel> source)
        {
            _source = source;
            _source.CollectionChanged += OnSourceChanged;
            foreach (var item in source)
            {
                if (item is not IWorkflowLinkViewModel)
                {
                    Add(item);
                }
            }
        }

        /// <summary>Unsubscribes from the source so this wrapper can be garbage-collected on session change.</summary>
        public void Detach() => _source.CollectionChanged -= OnSourceChanged;

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var item in e.NewItems ?? Array.Empty<object>())
                    {
                        if (item is not IWorkflowLinkViewModel)
                        {
                            Add((IWorkflowViewModel)item);
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (var item in e.OldItems ?? Array.Empty<object>())
                    {
                        if (item is not IWorkflowLinkViewModel)
                        {
                            Remove((IWorkflowViewModel)item);
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Clear();
                    foreach (var item in _source)
                    {
                        if (item is not IWorkflowLinkViewModel)
                        {
                            Add(item);
                        }
                    }
                    break;
            }
        }
    }
}
