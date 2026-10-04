using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Input;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class WorkflowView : ContentView
{
    private TreeViewModel _workflowViewModel = new();
    private bool _layoutRefreshPending;
    private Page MainPage => Application.Current?.Windows[0].Page ?? throw new InvalidOperationException();

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
        HookLinkKeys(_workflowViewModel);
        // MAUI propagates BindingContext through the visual tree automatically,
        // so setting it on the ContentView root is sufficient. Do NOT set
        // BindingContext on individual child elements — that breaks the natural
        // inheritance chain and can cause missed binding updates.
        BindingContext = _workflowViewModel;
        // Propagate the tree to the HUD explicitly so its BindingContextChanged fires even if
        // inheritance does not reach the nested overlay.
        InfoOverlay.BindingContext = _workflowViewModel;

        SyncLinkInput();

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

    // VeloxDev customization: 悬停高亮是这本 demo 的。overlay 默认什么都不画，订阅树的输入事件、
    // 把「现在轮到哪条线」交给它，它才照给定颜色画那一条 —— 换色/换画法都在这里改。
    private IWorkflowInputEvents? _linkInput;

    private void SyncLinkInput()
    {
        if (_linkInput is not null)
        {
            _linkInput = null;
        }

        if (BindingContext is IWorkflowTreeViewModel tree && tree.GetHelper() is IWorkflowInputEvents events)
        {
            _linkInput = events;
        }
    }



    // VeloxDev customization: Delete 归宿主 —— 库只把按键路由过来（target 就是指针停着的那条线），
    // 删不删由这里写（与悬停高亮同一条路）。
    private void HookLinkKeys(IWorkflowTreeViewModel tree)
    {
        if (tree.GetHelper() is not IWorkflowInputEvents events) return;

        events.Input.KeyDown += (_, e) =>
        {
            if (e.Key != WorkflowKey.Delete || e.Handle.PreventDefault) return;
            if (e.Target is not IWorkflowLinkViewModel link || !link.DeleteCommand.CanExecute(null)) return;

            link.DeleteCommand.Execute(null);
        };
    }
}
